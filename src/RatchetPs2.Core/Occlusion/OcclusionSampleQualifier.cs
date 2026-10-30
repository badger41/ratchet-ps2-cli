using System.Numerics;

namespace RatchetPs2.Core.Occlusion;

public readonly record struct OcclusionBlockingFace(Vector3 A, Vector3 B, Vector3 C, uint Rank);

/// <summary>Relocates buried cube corners and supplements affected cells with shared vertical-edge midpoints.</summary>
public sealed class OcclusionSampleQualifier
{
    private const float Clearance = .05f;
    private readonly OcclusionBlockingFace[] faces;
    private readonly (Vector3 Min, Vector3 Max)[] bounds;
    private readonly Dictionary<(int, int, int), List<int>> bins = [];
    private readonly List<int> large = [];
    private readonly Func<int, double, double, bool> opaque;

    public OcclusionSampleQualifier(IReadOnlyList<OcclusionBlockingFace> faces, Func<int, double, double, bool> opaque,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(opaque);
        token.ThrowIfCancellationRequested();
        this.faces = faces.ToArray(); this.opaque = opaque;
        bounds = new (Vector3, Vector3)[faces.Count];
        var references = 0;
        for (int i = 0; i < faces.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var f = faces[i];
            if (!Finite(f.A) || !Finite(f.B) || !Finite(f.C)) throw new InvalidDataException("Invalid occlusion blocker coordinates.");
            var lo = Vector3.Min(f.A, Vector3.Min(f.B, f.C)); var hi = Vector3.Max(f.A, Vector3.Max(f.B, f.C));
            bounds[i] = (lo, hi);
            int x0=Bin(lo.X-.001f), x1=Bin(hi.X+.001f), y0=Bin(lo.Y-.001f), y1=Bin(hi.Y+.001f), z0=Bin(lo.Z-.001f), z1=Bin(hi.Z+.001f);
            var count = (long)(x1-x0+1)*(y1-y0+1)*(z1-z0+1);
            // Keep very large faces and excess index entries in the bounded fallback list.
            if (count > 4096 || references + count > 4_000_000 || bins.Count >= 250_000) { large.Add(i); continue; }
            for (int z=z0; z<=z1; z++) for (int y=y0; y<=y1; y++) for (int x=x0; x<=x1; x++)
            {
                if (!bins.TryGetValue((x,y,z), out var list)) bins[(x,y,z)] = list = [];
                list.Add(i); references++;
            }
        }
    }

    /// <summary>Returns eight corners, followed by four vertical-edge midpoints if a corner moved; the center stays separate.</summary>
    public Vector3[] Qualify(Vector3 minimum, CancellationToken token = default, float centerPull = 0)
        => Qualify(minimum,out _,token,centerPull);

    /// <summary>Also reports corner bits whose repair used the inward-qualified local references.</summary>
    public Vector3[] Qualify(Vector3 minimum, out byte inwardCorners, CancellationToken token = default, float centerPull = 0)
    {
        inwardCorners=0;
        if (!float.IsFinite(centerPull) || centerPull is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(centerPull));
        if (!Finite(minimum) || minimum.X>999996 || minimum.Y>999996 || minimum.Z>999996)
            throw new InvalidDataException("Invalid occlusion cell coordinates.");
        token.ThrowIfCancellationRequested();
        var maximum=minimum+new Vector3(4); var center=minimum+new Vector3(2);
        var candidates = new HashSet<int>(large);
        for (int z=Bin(minimum.Z-.001f); z<=Bin(maximum.Z+.001f); z++)
            for (int y=Bin(minimum.Y-.001f); y<=Bin(maximum.Y+.001f); y++)
                for (int x=Bin(minimum.X-.001f); x<=Bin(maximum.X+.001f); x++)
                    if (bins.TryGetValue((x,y,z), out var list)) candidates.UnionWith(list);
        candidates.RemoveWhere(i =>
        {
            var overlap=Vector3.Min(bounds[i].Max-minimum,maximum-bounds[i].Min);
            return overlap.X<-.001 || overlap.Y<-.001 || overlap.Z<-.001;
        });
        var corners = new Vector3[8]; var cursor=0;
        for (int z=0; z<=4; z+=4) for (int y=0; y<=4; y+=4) for (int x=0; x<=4; x+=4)
            corners[cursor++]=minimum+new Vector3(x,y,z);
        var nativeCorners=corners.ToArray();
        var anchor=center;
        var scan=Scan(center,corners,candidates,token); var initial=scan;
        if (scan.First.Any(h=>h is {Front:false}))
        {
            var distance=float.PositiveInfinity;
            for (int i=0; i<8; i++)
            {
                if (initial.First[i] is not {Front:false} hit) continue;
                var proposal=center+Vector3.Normalize(corners[i]-center)*(float)hit.Distance+Normal(hit)*Clearance;
                if (proposal.X<minimum.X+.001 || proposal.Y<minimum.Y+.001 || proposal.Z<minimum.Z+.001
                    || proposal.X>maximum.X-.001 || proposal.Y>maximum.Y-.001 || proposal.Z>maximum.Z-.001) continue;
                var proposed=Scan(proposal,corners,candidates,token);
                if (proposed.First.Any(h=>h is {Front:false})) continue;
                var movement=Vector3.DistanceSquared(center,proposal);
                if (movement>=distance) continue;
                anchor=proposal; scan=proposed; distance=movement;
            }
            if (!float.IsFinite(distance)) return corners;
        }
        var projected=new Vector3?[8]; var hits=new Hit?[8];
        var localAnchors=anchor!=center ? new List<Vector3>() : null;
        for (int i=0; i<8; i++)
        {
            // The last center-to-corner hit is the first inward hit, with facing reversed.
            if (localAnchors is not null && initial.Last[i] is not {Front:true}
                && !Scan(nativeCorners[i],nativeCorners,candidates,token).First.Any(h=>h is {Front:false}))
                localAnchors.Add(nativeCorners[i]);
            if (initial.First[i] is null || scan.Last[i] is not {Front:true} hit) continue;
            hits[i]=hit;
            projected[i]=Project(nativeCorners[i],Normal(hit),faces[hit.Face].A,minimum,maximum);
            if (localAnchors is not null && projected[i] is {} point
                && !Scan(point,nativeCorners,candidates,token).First.Any(h=>h is {Front:false})
                && !localAnchors.Contains(point)) localAnchors.Add(point);
        }
        var moved=false;
        for (int i=0; i<8; i++)
        {
            if (projected[i] is not {} point || point==nativeCorners[i]) continue;
            var repaired=ClearProjection(point,anchor,nativeCorners,minimum,maximum,candidates,token);
            var distance=Vector3.DistanceSquared(repaired,nativeCorners[i]);
            if (localAnchors is not null) foreach (var local in localAnchors)
            {
                var candidate=ClearProjection(point,local,nativeCorners,minimum,maximum,candidates,token);
                var movement=Vector3.DistanceSquared(candidate,nativeCorners[i]);
                if (movement>=distance-.000001f) continue;
                repaired=candidate; distance=movement; inwardCorners|=(byte)(1<<i);
            }
            corners[i]=centerPull==0 ? repaired : PullIn(repaired,center,minimum,hits[i]!.Value,centerPull,candidates,token);
            moved|=corners[i]!=nativeCorners[i];
        }
        if (!moved) return corners;
        // Keep side coverage at mid-height when floors or walls remove the original corner views.
        return [..corners,minimum+new Vector3(0,0,2),minimum+new Vector3(4,0,2),
            minimum+new Vector3(0,4,2),minimum+new Vector3(4,4,2)];
    }

    public static Vector3 InsetBarrierBoundary(Vector3 point, float centerHeight, IReadOnlyList<float> heights)
    {
        ArgumentNullException.ThrowIfNull(heights);
        if (!Finite(point) || !float.IsFinite(centerHeight) || Math.Abs(centerHeight)>1000000)
            throw new InvalidDataException("Invalid barrier sample coordinates.");
        foreach (var height in heights)
        {
            if (!float.IsFinite(height) || Math.Abs(height)>1000000)
                throw new InvalidDataException("Invalid occlusion barrier height.");
            if (point.Z!=height || centerHeight==height) continue;
            // A shared point on the plane must sample the side belonging to this cell.
            point.Z=centerHeight>height ? Math.Max(height+.01f,MathF.BitIncrement(height))
                : Math.Min(height-.01f,MathF.BitDecrement(height));
        }
        return point;
    }

    private Vector3 ClearProjection(Vector3 point, Vector3 anchor, Vector3[] corners, Vector3 minimum, Vector3 maximum,
        HashSet<int> candidates, CancellationToken token)
    {
        // A rock can split the cell: a locally clear projection need not share the center's repaired airspace.
        if (!Scan(point,corners,candidates,token).First.Any(h=>h is {Front:false})) return point;
        // A normal projection can clear one face while leaving the sample behind a different wall or floor.
        for (int attempt=0; attempt<8; attempt++)
        {
            var reachable=LimitSegment(anchor,point,candidates,token,out var wall);
            if (wall is null) return point;
            var hit=wall.Value;
            var normal=hit.Front ? Normal(hit) : -Normal(hit);
            var projected=Project(point,normal,faces[hit.Face].A,minimum,maximum);
            if (attempt==7 || projected is null || projected.Value==point) return reachable;
            point=projected.Value;
        }
        return anchor;
    }

    private Vector3 PullIn(Vector3 point, Vector3 center, Vector3 minimum, Hit surface, float amount,
        HashSet<int> candidates, CancellationToken token)
    {
        var target=center;
        // Pull-in may slide around a blocker, but never past the center or away along any cube axis.
        var lo=Vector3.Min(point,center)-minimum; var hi=Vector3.Max(point,center)-minimum;
        List<(Vector3 Normal,float Offset)>? planes=null;
        for (int attempt=0; attempt<4; attempt++)
        {
            var reachable=LimitSegment(point,target,candidates,token,out var wall);
            if (wall is null || attempt==3) return Vector3.Lerp(point,reachable,amount);
            var normal=wall.Value.Front ? Normal(wall.Value) : -Normal(wall.Value);
            // Keep the target in the clear portion of the cell when its native center is behind a wall.
            planes ??= [(Vector3.UnitX,lo.X),(-Vector3.UnitX,-hi.X),(Vector3.UnitY,lo.Y),(-Vector3.UnitY,-hi.Y),
                (Vector3.UnitZ,lo.Z),(-Vector3.UnitZ,-hi.Z),
                (Normal(surface),Vector3.Dot(Normal(surface),faces[surface.Face].A-minimum)+Clearance)];
            planes.Add((normal,Vector3.Dot(normal,faces[wall.Value.Face].A-minimum)+Clearance));
            var interior=InteriorTarget(minimum,planes);
            if (interior is null || Vector3.DistanceSquared(interior.Value,target)<.00000001f)
                return Vector3.Lerp(point,reachable,amount);
            target=interior.Value;
        }
        return point;
    }

    private Vector3 LimitSegment(Vector3 point, Vector3 target, HashSet<int> candidates, CancellationToken token, out Hit? wall)
    {
        wall=null;
        var delta=target-point; var length=delta.Length();
        if (length<.00001f) return point;
        var direction=delta/length; var distance=length; var tests=0;
        foreach (var index in candidates)
        {
            if ((tests++ & 255)==0) token.ThrowIfCancellationRequested();
            var hit=Intersect(point,direction,faces[index],index);
            // Once the projection is clear, neither side of an opaque face is safe to cross.
            if (!double.IsFinite(hit.Distance) || hit.Distance<0) continue;
            var clearance=Clearance/Math.Max(.00001f,Math.Abs(Vector3.Dot(Normal(hit),direction)));
            if (hit.Distance>distance+clearance || !opaque(index,hit.U,hit.V)) continue;
            var allowed=Math.Max(0,(float)hit.Distance-clearance);
            if (allowed<distance || allowed==distance && wall is {} previous && faces[index].Rank<faces[previous.Face].Rank)
            { distance=allowed; wall=hit; }
        }
        return point+direction*distance;
    }

    private static Vector3? InteriorTarget(Vector3 minimum, IReadOnlyList<(Vector3 Normal,float Offset)> planes)
    {
        var vertices=new List<Vector3>();
        // Vertices of the cube clipped by the blocking planes bound a convex region; their mean stays inside it.
        for (int a=0; a<planes.Count-2; a++) for (int b=a+1; b<planes.Count-1; b++) for (int c=b+1; c<planes.Count; c++)
        {
            var p=planes[a]; var q=planes[b]; var r=planes[c];
            var qr=Vector3.Cross(q.Normal,r.Normal); var determinant=Vector3.Dot(p.Normal,qr);
            if (Math.Abs(determinant)<.000001f) continue;
            var vertex=(qr*p.Offset+Vector3.Cross(r.Normal,p.Normal)*q.Offset+Vector3.Cross(p.Normal,q.Normal)*r.Offset)/determinant;
            if (planes.Any(plane=>Vector3.Dot(plane.Normal,vertex)<plane.Offset-.0001f)
                || vertices.Any(v=>Vector3.DistanceSquared(v,vertex)<.00000001f)) continue;
            vertices.Add(vertex);
        }
        if (vertices.Count==0) return null;
        var sum=Vector3.Zero; foreach (var vertex in vertices) sum+=vertex;
        return minimum+Vector3.Clamp(sum/vertices.Count,Vector3.Zero,new Vector3(4));
    }

    // shortcut: Open meshes can make the chosen side ambiguous; use gameplay regressions to refine qualification.
    private static Vector3? Project(Vector3 point, Vector3 normal, Vector3 surface, Vector3 minimum, Vector3 maximum)
    {
        static double Dot(Vector3 a, Vector3 b) => (double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z;
        var required=Dot(normal,surface)+.05;
        var furthest=new Vector3(normal.X>=0?maximum.X:minimum.X,normal.Y>=0?maximum.Y:minimum.Y,normal.Z>=0?maximum.Z:minimum.Z);
        if (Dot(normal,furthest)<required) return null;
        if (Dot(normal,point)>=required) return point;
        double bound=1;
        foreach (var (n,p,lo,hi) in new[] { (normal.X,point.X,minimum.X,maximum.X), (normal.Y,point.Y,minimum.Y,maximum.Y), (normal.Z,point.Z,minimum.Z,maximum.Z) })
            if (n!=0) bound=Math.Max(bound,Math.Abs(((n>0?hi:lo)-p)/n));
        Vector3 At(double amount) => new(
            (float)Math.Clamp(point.X+amount*normal.X,minimum.X,maximum.X),
            (float)Math.Clamp(point.Y+amount*normal.Y,minimum.Y,maximum.Y),
            (float)Math.Clamp(point.Z+amount*normal.Z,minimum.Z,maximum.Z));
        double left=0,right=bound;
        // The clamped normal displacement is monotonic; find the closest feasible point in the cube.
        for (int i=0; i<64; i++)
        {
            var middle=(left+right)*.5;
            if (Dot(normal,At(middle))<required) left=middle; else right=middle;
        }
        return At(right);
    }

    private (Hit?[] First, Hit?[] Last) Scan(Vector3 anchor, Vector3[] corners, HashSet<int> candidates, CancellationToken token)
    {
        var first=new Hit?[8]; var last=new Hit?[8];
        for (int corner=0; corner<8; corner++)
        {
            token.ThrowIfCancellationRequested();
            var delta=corners[corner]-anchor; var distance=delta.Length();
            if (distance<.00001f) continue;
            var direction=delta/distance;
            var length=distance+.0001; var tests=0;
            foreach (var index in candidates)
            {
                if ((tests++ & 255)==0) token.ThrowIfCancellationRequested();
                var h=Intersect(anchor,direction,faces[index],index);
                if (h.Distance<.00001 || h.Distance>length || !opaque(index,h.U,h.V)) continue;
                var near=first[corner]; var far=last[corner];
                if (near is null || h.Distance<near.Value.Distance || Math.Abs(h.Distance-near.Value.Distance)<1e-7 && faces[index].Rank<faces[near.Value.Face].Rank) first[corner]=h;
                if (far is null || h.Distance>far.Value.Distance || Math.Abs(h.Distance-far.Value.Distance)<1e-7 && faces[index].Rank<faces[far.Value.Face].Rank) last[corner]=h;
            }
        }
        return (first,last);
    }

    private Vector3 Normal(Hit h)
    {
        var f=faces[h.Face];
        return Vector3.Normalize(Vector3.Cross(f.B-f.A,f.C-f.A));
    }

    private static Hit Intersect(Vector3 origin, Vector3 direction, OcclusionBlockingFace face, int index)
    {
        double ex=(double)face.B.X-face.A.X,ey=(double)face.B.Y-face.A.Y,ez=(double)face.B.Z-face.A.Z;
        double fx=(double)face.C.X-face.A.X,fy=(double)face.C.Y-face.A.Y,fz=(double)face.C.Z-face.A.Z;
        double px=direction.Y*fz-direction.Z*fy,py=direction.Z*fx-direction.X*fz,pz=direction.X*fy-direction.Y*fx;
        var det=ex*px+ey*py+ez*pz;
        if (Math.Abs(det)<1e-12) return new(double.PositiveInfinity,0,0,false,index);
        double dx=(double)origin.X-face.A.X,dy=(double)origin.Y-face.A.Y,dz=(double)origin.Z-face.A.Z;
        var u=(dx*px+dy*py+dz*pz)/det;
        double qx=dy*ez-dz*ey,qy=dz*ex-dx*ez,qz=dx*ey-dy*ex;
        var v=(direction.X*qx+direction.Y*qy+direction.Z*qz)/det;
        if (u< -1e-8 || v< -1e-8 || u+v>1+1e-8) return new(double.PositiveInfinity,0,0,false,index);
        return new((fx*qx+fy*qy+fz*qz)/det,u,v,det>0,index);
    }

    private static int Bin(float v) => (int)MathF.Floor(v/16);
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
        && Math.Abs(p.X)<=1000000 && Math.Abs(p.Y)<=1000000 && Math.Abs(p.Z)<=1000000;
    private readonly record struct Hit(double Distance, double U, double V, bool Front, int Face);
}
