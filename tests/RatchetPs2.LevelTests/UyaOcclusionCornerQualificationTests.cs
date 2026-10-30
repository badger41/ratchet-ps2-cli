using RatchetPs2.Core.Occlusion;
using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Geometry;


internal static class UyaOcclusionCornerQualificationTests
{
    internal static void Run()
    {
        CheckRockSeparatedCorners();
        // Hoven #6267: all 33 overlapping triangles have uniform alpha 128.
        var rows=JsonSerializer.Deserialize<float[][]>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "OcclusionCorner6267.json")))!;
        var faces=rows.Select(v=>new OcclusionBlockingFace(new(v[0],v[1],v[2]),new(v[3],v[4],v[5]),
            new(v[6],v[7],v[8]),(uint)v[9])).ToArray();
        var qualifier=new OcclusionSampleQualifier(faces,(_,_,_)=>true);
        var minimum=new Vector3(296,188,72);
        var anchor=new Vector3(297.92468f,190.11554f,74.09389f);
        var oldPoint=new Vector3(296.76022f,188.72f,73.830505f);
        Check(faces.Length==33 && faces.Any(f=>Crosses(anchor,oldPoint,f)),
            "The fixture must reproduce the old sample behind the tunnel walls.");
        var baseline=qualifier.Qualify(minimum);
        for (int step=0; step<=5; step++)
        {
            var points=qualifier.Qualify(minimum,centerPull:step*.18f);
            Check(points.Length==12,"Affected cells must keep their supplementary edge coverage.");
            foreach (var slot in new[]{0,2,3,6})
            {
                Check(!faces.Any(f=>Crosses(anchor,points[slot],f)),
                    $"Corner {slot} remains behind opaque geometry at pull-in {step}.");
                Check(!faces.Any(f=>Crosses(baseline[slot],points[slot],f)),
                    $"Pull-in {step} crossed an opaque face from its repaired projection at corner {slot}.");
            }
            foreach (var slot in new[]{1,4,5,7})
                Check(points[slot]==minimum+new Vector3((slot&1)*4,((slot>>1)&1)*4,((slot>>2)&1)*4),
                    "The fix must preserve unqualified native corners.");
            Check(points.All(p=>p.X>=296&&p.X<=300&&p.Y>=188&&p.Y<=192&&p.Z>=72&&p.Z<=76),
                "Repaired cameras must remain within the native octant.");
        }
        Console.WriteLine("PASS Hoven octant #6267 multi-face projection and two-sided pull-in regression");
    }

    private static void CheckRockSeparatedCorners()
    {
        // Hoven #6404: all 42 local blocking faces have uniform alpha 128.
        var rows=JsonSerializer.Deserialize<float[][]>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "OcclusionCorner6404.json")))!;
        var faces=rows.Select(v=>new OcclusionBlockingFace(new(v[0],v[1],v[2]),new(v[3],v[4],v[5]),
            new(v[6],v[7],v[8]),(uint)v[9])).ToArray();
        var qualifier=new OcclusionSampleQualifier(faces,(_,_,_)=>true);
        var minimum=new Vector3(280,204,72);
        var baseline=qualifier.Qualify(minimum);
        for(int step=0;step<=5;step++)
        {
            var points=qualifier.Qualify(minimum,out var inwardCorners,centerPull:step*.18f);
            Check((inwardCorners&13)==13,"Inward checks must report the three lower corners repaired using local references.");
            Check(points.Length==12,"The rock case must retain all corner and supplementary edge samples.");
            Check(points[6].X<280.02f && points[6].Y>207 && points[6].Z>75.5f,
                $"Pull-in {step} dragged a clear local corner across the octant behind the rock.");
            Check(points[5]==new Vector3(284,204,76),"The rock fix must retain the clear native corner.");
            Check(points[0].X<282.5f && points[0].Z<74.1f && points[2].X<282 && points[2].Y>207,
                $"Pull-in {step} dragged the below-floor samples past the center and behind the rock.");
            for(int slot=0;slot<8;slot++)
            {
                var lo=Vector3.Min(baseline[slot],minimum+new Vector3(2))-new Vector3(.0001f);
                var hi=Vector3.Max(baseline[slot],minimum+new Vector3(2))+new Vector3(.0001f);
                Check(points[slot].X>=lo.X&&points[slot].X<=hi.X&&points[slot].Y>=lo.Y&&points[slot].Y<=hi.Y&&points[slot].Z>=lo.Z&&points[slot].Z<=hi.Z,
                    "The slider must stay between each repaired sample and the center on every axis.");
                Check(!faces.Any(f=>Crosses(baseline[slot],points[slot],f)),"Additional pull-in must not cross a rock or terrain face.");
            }
            Check(points.All(p=>p.X>=280&&p.X<=284&&p.Y>=204&&p.Y<=208&&p.Z>=72&&p.Z<=76),
                "Local repairs must remain inside octant #6404.");
        }
        var plan=OcclusionSamplePlanner.PlanCells([new(70,51,18,false)],qualifier:qualifier,samplePullIn:2);
        Check((plan.Cells[0].InwardCorners&13)==13,"The actual bake and preview plan must carry inward repair diagnostics.");
        Console.WriteLine("PASS Hoven octant #6404 keeps qualified cameras on the local side of the rock");
    }

    private static bool Crosses(Vector3 start, Vector3 end, OcclusionBlockingFace face)
    {
        var direction=end-start; var e1=face.B-face.A; var e2=face.C-face.A;
        var p=Vector3.Cross(direction,e2); var determinant=Vector3.Dot(e1,p);
        if (Math.Abs(determinant)<.000001f) return false;
        var delta=start-face.A; var u=Vector3.Dot(delta,p)/determinant;
        var q=Vector3.Cross(delta,e1); var v=Vector3.Dot(direction,q)/determinant;
        var distance=Vector3.Dot(e2,q)/determinant;
        return u>=0 && v>=0 && u+v<=1 && distance>.00001f && distance<.99999f;
    }

    private static void Check(bool value,string message) { if (!value) throw new Exception(message); }
}
