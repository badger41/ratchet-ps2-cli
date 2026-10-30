using System.Numerics;
using RatchetPs2.Core.Occlusion;

internal static class OcclusionSampleQualificationTests
{
    internal static void Run()
    {
        OcclusionBlockingFace[] Plane(float z, bool up = true)
        {
            var a=new Vector3(-20,-20,z); var b=new Vector3(20,-20,z); var c=new Vector3(20,20,z); var d=new Vector3(-20,20,z);
            return up ? [new(a,b,c,1),new(a,c,d,2)] : [new(a,c,b,1),new(a,d,c,2)];
        }
        Vector3[] Native(Vector3 lo) => new OcclusionSampleQualifier([], (_,_,_)=>true).Qualify(lo);
        var floor = new OcclusionSampleQualifier(Plane(1), (_,_,_)=>true);
        var points=floor.Qualify(Vector3.Zero);
        Vector3[] midpoints=[new(0,0,2),new(4,0,2),new(0,4,2),new(4,4,2)];
        Check(Native(Vector3.Zero).Length==8, "Clear cells must retain only the original eight corners.");
        Check(points.Length==12 && points.Skip(8).SequenceEqual(midpoints),
            "A moved corner must append all four native vertical-edge midpoints after the original corners.");
        for (int i=0; i<4; i++) Check(Math.Abs(points[i].Z-1.05f)<.0001, "Buried corners must stop above the floor.");
        for (int i=0; i<4; i++) Check(points[i].X==Native(Vector3.Zero)[i].X && points[i].Y==Native(Vector3.Zero)[i].Y,
            "Pulling above a floor must preserve each corner's horizontal coverage.");
        for (int i=4; i<8; i++) Check(points[i]==Native(Vector3.Zero)[i], "Clear corners must remain shared.");
        var previous=points;
        for (int step=0; step<=5; step++)
        {
            var pulled=floor.Qualify(Vector3.Zero,centerPull:step*.18f);
            Check(pulled.Skip(8).SequenceEqual(midpoints), "Pull-in must not collapse the supplementary edge coverage.");
            for (int i=0; i<4; i++)
            {
                var expected=Vector3.Lerp(points[i],new Vector3(2),step*.18f);
                Check(Vector3.Distance(pulled[i],expected)<.00001f, "Pull-in must interpolate relocated corners toward the center.");
                Check(Vector3.Distance(pulled[i],new Vector3(2))<=Vector3.Distance(previous[i],new Vector3(2))+.00001f,
                    "Each pull-in step must move monotonically toward the center.");
            }
            for (int i=4; i<8; i++) Check(pulled[i]==points[i], "Pull-in must preserve clear corners.");
            var planned=OcclusionSamplePlanner.PlanCells([new(0,0,0,false)],qualifier:floor,samplePullIn:step);
            for (int i=0; i<8; i++) Check(new Vector3(planned.Origins[planned.Cells[0].Samples[i+1]])==pulled[i],
                "Host preview and bake planning must use the same normalized pull-in.");
            Check(planned.Cells[0].Samples.Count==13 && planned.Cells[0].Samples.Skip(9)
                .Select(id=>new Vector3(planned.Origins[id])).SequenceEqual(midpoints),
                "Preview and bake planning must include all supplementary edge cameras.");
            previous=pulled;
        }
        var airThroughWall = new OcclusionSampleQualifier([..Plane(.5f,false),..Plane(1.5f)], (_,_,_)=>true);
        Check(airThroughWall.Qualify(Vector3.Zero).SequenceEqual(Native(Vector3.Zero)), "A corner beyond a closed slab ends in air and must stay put.");
        var diagonalWall = new OcclusionSampleQualifier([new(new(-20,23,-20),new(-20,23,20),new(23,-20,20),1)],(_,_,_)=>true);
        var edge=diagonalWall.Qualify(Vector3.Zero)[0];
        Check(Math.Abs(edge.X-edge.Y)<.0001 && Math.Abs(edge.X+edge.Y-(3+Math.Sqrt(2)*.05))<.0001 && edge.Z==0,
            "A rotated wall must move corners perpendicular to its face while preserving vertical coverage.");
        Check(diagonalWall.Qualify(Vector3.Zero).Skip(8).SequenceEqual(midpoints), "Rotated walls must also trigger edge coverage.");
        var buriedCenter = new OcclusionSampleQualifier(Plane(2.5f), (_,_,_)=>true).Qualify(Vector3.Zero);
        Check(buriedCenter.SequenceEqual(Native(Vector3.Zero)), "Repairing an anchor cannot establish that its original clear corner segments end inside an open surface.");
        for (int i=4; i<8; i++) Check(buriedCenter[i]==Native(Vector3.Zero)[i], "Anchor repair must preserve clear upper corners.");
        var wall=new OcclusionBlockingFace[]
        {
            new(new(2.5f,-20,-20),new(2.5f,20,-20),new(2.5f,20,20),3),
            new(new(2.5f,-20,-20),new(2.5f,20,20),new(2.5f,-20,20),4),
        };
        var repaired=new OcclusionSampleQualifier([..Plane(1),..wall],(_,_,_)=>true).Qualify(Vector3.Zero);
        Check(repaired.Take(4).All(p=>Math.Abs(p.Z-1.05f)<.0001), "Anchor repair must still relocate corners whose original segment crosses the floor.");
        foreach(var slot in new[]{4,6}) Check(repaired[slot]==Native(Vector3.Zero)[slot], "A replacement anchor must not move clear corners across the wall into another space.");
        var guarded=new OcclusionSampleQualifier([..Plane(1),..wall],(_,_,_)=>true).Qualify(Vector3.Zero,centerPull:.9f);
        foreach(var i in new[]{1,3}) Check(guarded[i].X>=2.5499f && guarded[i].Z>=1.0499f,
            "Pull-in must retain clearance instead of crossing an opaque wall to reach a buried center.");
        foreach(var i in new[]{1,3}) Check(guarded[i].X>3 && guarded[i].Z>1.3f,
            "Maximum pull-in must target the clear cell interior instead of stopping against a wall in front of the native center.");
        var lowerCeiling=new OcclusionSampleQualifier([..Plane(1),..Plane(1.5f,false)],(_,_,_)=>true).Qualify(Vector3.Zero,centerPull:.9f);
        Check(lowerCeiling.Take(4).All(p=>p.Z<=1.4501f), "Additional pull-in must stop before a second opaque surface.");
        Check(lowerCeiling.Take(4).All(p=>p.Z>1.1f && p.Z<1.4f), "Multiple blocking planes must put the target inside the corridor, away from either surface.");
        var exitWall=wall.Select(f=>f with { A=f.A-new Vector3(2.3f,0,0),B=f.B-new Vector3(2.3f,0,0),C=f.C-new Vector3(2.3f,0,0) }).ToArray();
        var exitProjection=new OcclusionSampleQualifier([..Plane(1),..exitWall],(_,_,_)=>true);
        Check(exitProjection.Qualify(Vector3.Zero)[0].Z>=1.0499f,
            "A projection that clears a wall must also clear the secondary floor before applying pull-in.");
        Check(exitProjection.Qualify(Vector3.Zero,centerPull:.9f)[0].Z>1.5f,
            "Repairing the projection must preserve useful pull-in toward the clear center.");
        var alphaCeiling=new OcclusionSampleQualifier([..Plane(1),..Plane(1.5f,false)],(face,_,_)=>face<2).Qualify(Vector3.Zero,centerPull:.9f);
        Check(alphaCeiling.Take(4).All(p=>Math.Abs(p.Z-1.905f)<.0001f), "Transparent texels must let additional pull-in pass through.");
        var transparent = new OcclusionSampleQualifier(Plane(1), (_,_,_)=>false);
        Check(transparent.Qualify(Vector3.Zero).SequenceEqual(Native(Vector3.Zero)), "Transparent faces must not relocate cameras.");
        Check(transparent.Qualify(Vector3.Zero,centerPull:.9f).SequenceEqual(Native(Vector3.Zero)), "Transparent faces must not trigger additional pull-in.");
        var translated = new OcclusionSampleQualifier(Plane(-3), (_,_,_)=>true).Qualify(new(-4,-4,-4));
        Check(translated.Take(4).All(p=>Math.Abs(p.Z+2.95f)<.0001), "Negative cell coordinates must use the same surface clearance.");
        Check(translated.Skip(8).SequenceEqual(midpoints.Select(p=>p-new Vector3(4))), "Supplementary samples must translate with their cell.");
        var largeFace = new OcclusionSampleQualifier([new(new(-2000,-2000,1),new(2000,-2000,1),new(0,2000,1),1)], (_,_,_)=>true);
        Check(largeFace.Qualify(Vector3.Zero).Take(4).All(p=>Math.Abs(p.Z-1.05f)<.0001), "Faces beyond the spatial-bin budget must remain blockers.");
        var adjacent=OcclusionSamplePlanner.PlanCells([new(0,0,0,false),new(1,0,0,true)],qualifier:floor);
        Check(adjacent.Cells.All(c=>c.Samples.Count==13), "Only cells with moved corners must gain four sample slots.");
        Check(adjacent.Cells[0].Samples.Intersect(adjacent.Cells[1].Samples).Count()==6, "Adjacent qualified cells must share corner and midpoint cameras.");
        var mixed=OcclusionSamplePlanner.PlanCells([new(0,0,0,false),new(0,0,1,false)],qualifier:floor);
        Check(mixed.Cells.Select(c=>c.Samples.Count).SequenceEqual(new[]{13,9}), "Qualification must not add views to a clear neighboring cell.");
        var water=OcclusionSamplePlanner.PlanCells([new(0,0,0,false),new(0,0,-1,false)],barrierHeights:[0]);
        foreach (var cell in water.Cells)
        {
            var z=water.Origins[cell.Samples[0]][2];
            Check(z==cell.Z*4+2,"Water must retain native centers.");
            Check(cell.Samples.Select(id=>water.Origins[id][2]).All(v=>z>0?v>0:v<0),
                "A boundary corner must stay on its cell's side of the barrier.");
        }
        Check(water.Cells.SelectMany(c=>c.Samples).Distinct().Count()==water.Origins.Count,"Boundary adjustments must not leave unused GPU origins.");
        Check(OcclusionSampleQualifier.InsetBarrierBoundary(new(1,1,2),2,[2])==new Vector3(1,1,2),
            "A native center on the plane spans both sides and must remain unchanged.");
        foreach (var invalid in new IReadOnlyList<float>[] { [float.NaN], [0,0], Enumerable.Range(0,17).Select(i=>(float)i).ToArray() })
            Throws<InvalidDataException>(()=>OcclusionSamplePlanner.PlanCells([new(0,0,0,false)],barrierHeights:invalid));
        var touching=OcclusionSamplePlanner.PlanCells([new(-1,-1,0,false),new(0,-1,0,false),new(-1,0,0,false),new(0,0,0,false)],qualifier:floor);
        var sharedEdge=Enumerable.Range(0,touching.Origins.Count).Single(i=>touching.Origins[i].SequenceEqual(new float[]{0,0,2}));
        Check(touching.Cells.All(c=>c.Samples.Skip(9).Contains(sharedEdge)), "One vertical-edge midpoint must serve all four touching qualified octants.");
        foreach (var cell in adjacent.Cells)
        {
            var center=adjacent.Origins[cell.Samples[0]];
            Check(center.SequenceEqual(new float[]{cell.X*4+2,cell.Y*4+2,cell.Z*4+2}), "Center refinement must retain its native origin.");
            foreach (var id in cell.Samples)
                for (int a=0; a<3; a++)
                {
                    var lo=new[]{cell.X*4,cell.Y*4,cell.Z*4}[a];
                    Check(adjacent.Origins[id][a]>=lo && adjacent.Origins[id][a]<=lo+4, "Adjusted corners must stay within their cell.");
                }
        }
        using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
        Throws<OperationCanceledException>(()=>floor.Qualify(Vector3.Zero,cancelled.Token));
        Throws<OperationCanceledException>(()=>new OcclusionSampleQualifier(Plane(1),(_,_,_)=>true,cancelled.Token));
        Throws<InvalidDataException>(()=>new OcclusionSampleQualifier([new(new(float.NaN,0,0),Vector3.UnitX,Vector3.UnitY,1)],(_,_,_)=>true));
        foreach(var invalid in new[]{float.NaN,-.1f,1.1f})
            Throws<ArgumentOutOfRangeException>(()=>floor.Qualify(Vector3.Zero,centerPull:invalid));
        foreach(var invalid in new[]{-1,6})
            Throws<ArgumentOutOfRangeException>(()=>OcclusionSamplePlanner.PlanCells([new(0,0,0,false)],samplePullIn:invalid));
        using var midway=new CancellationTokenSource();
        Throws<OperationCanceledException>(()=>OcclusionSamplePlanner.PlanCells(Enumerable.Range(0,130).Select(x=>new OcclusionOctant(x,0,0,false)).ToArray(),midway.Token,
            transparent,(done,_)=>{if(done==128)midway.Cancel();}));
        Console.WriteLine("PASS shared occlusion corner qualification, slab exits, anchor repair and cancellation");
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
}
