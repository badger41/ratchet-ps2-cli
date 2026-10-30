using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Core.Occlusion;
using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Gameplay;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Level;


internal static class UyaOcclusionOceanTests
{
    internal static void Run()
    {
        Dictionary<string,byte[]> Files(float height,int length=96)
        {
            var mobys=new byte[UyaMobyInstancesReader.HeaderSize+2*UyaMobyInstancesReader.RecordSize];
            BinaryPrimitives.WriteInt32LittleEndian(mobys,2);
            for (int i=0;i<2;i++)
            {
                var record=mobys.AsSpan(16+i*UyaMobyInstancesReader.RecordSize);
                BinaryPrimitives.WriteInt32LittleEndian(record,UyaMobyInstancesReader.RecordSize);
                BinaryPrimitives.WriteInt32LittleEndian(record[0x28..],0x0B37);
                BinaryPrimitives.WriteSingleLittleEndian(record[0x48..],999);
                BinaryPrimitives.WriteInt32LittleEndian(record[0x68..],0);
            }
            var data=new byte[length];
            if (length>=80) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(76),height);
            var tables=GameplayPvarTableWriter.Write([new(data,[],[])]);
            return new()
            {
                ["gameplay/core/moby_instances.bin"]=mobys,
                ["gameplay/core/pvar_moby_links.bin"]=tables.MobyLinksBytes,
                ["gameplay/core/pvar_table.bin"]=tables.TableBytes,
                ["gameplay/core/pvar_data.bin"]=tables.DataBytes,
                ["gameplay/core/pvar_relative_pointers.bin"]=tables.RelativePointerBytes,
            };
        }
        Check(UyaOcclusionOceanPlanes.ReadHeights(Files(61.35f)).SequenceEqual(new[]{61.35f}),
            "Ocean barriers must use PVar zPosition, deduplicate instances, and ignore Moby transform Z.");
        Check(UyaOcclusionOceanPlanes.ReadHeights(new Dictionary<string,byte[]>()).Length==0,"Levels without ocean must have no barrier.");
        foreach (var bad in new[]{float.NaN,float.PositiveInfinity,1000001}) Throws<InvalidDataException>(()=>UyaOcclusionOceanPlanes.ReadHeights(Files(bad)));
        Throws<InvalidDataException>(()=>UyaOcclusionOceanPlanes.ReadHeights(Files(0,79)));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Throws<OperationCanceledException>(()=>UyaOcclusionOceanPlanes.ReadHeights(Files(0),cancelled.Token));

        OcclusionSurfaceTriangle Target(float z,int group)=>new(new(-4,-4,z),new(4,-4,z),new(0,4,z),group,2);
        bool Has(byte[] mask,int group)=>(mask[group/8]&(1<<(group%8)))!=0;
        foreach (var side in new[]{-1f,1f})
        {
            var eye=new Vector3(0,0,side*2);var direction=new Vector3(0,0,-side);
            var result=UyaOcclusionRayReference.TraceCalibrated([Target(side,6),Target(-side,7)],[(eye,direction)],[],
                [new(-1,8,[0,0,-side],.1f)],backfaceExitDistance:1.5f,barrierHeights:[0])[0];
            Check(Has(result.Geometry,6)&&!Has(result.Geometry,7)&&!Has(result.Mobys,8),
                "Ocean must stop geometry and mobys across the plane from either side while retaining same-side targets.");
            var boundary=UyaOcclusionRayReference.TraceCalibrated([Target(-side,7)],[(new Vector3(0,0,side*.01f),direction)],[],[],
                backfaceExitDistance:1.5f,barrierHeights:[0])[0];
            Check(!Has(boundary.Geometry,7),"Nearby-backface allowance must never bypass an ocean crossing.");
            var straddling=UyaOcclusionRayReference.TraceCalibrated([Target(side,6)],[(Vector3.Zero,new Vector3(0,0,side))],[],[],
                barrierHeights:[0])[0];
            Check(Has(straddling.Geometry,6),"A center on the plane belongs to a straddling octant and must retain both sides.");
        }
        foreach (var bad in new IReadOnlyList<float>[] { [float.NaN], [0,0], Enumerable.Range(0,17).Select(i=>(float)i).ToArray() })
            Throws<InvalidDataException>(()=>UyaOcclusionRayReference.TraceCalibrated([],[],[],[],barrierHeights:bad));
        Console.WriteLine("PASS ocean PVar height, two-sided hard barrier, boundary and moby visibility");
    }

    private static void Check(bool value,string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
}
