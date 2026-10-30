using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Core.Occlusion;
using System.Buffers.Binary;
using RatchetPs2.Games.UYA.Level;
internal static class UyaOcclusionFullGridTests
{
    public static void Run()
    {
        var source = new byte[64 + 128];
        void U16(int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(at, 2), checked((ushort)value));
        BinaryPrimitives.WriteInt32LittleEndian(source, 64); U16(4, 3); U16(6, 1); U16(8, 4);
        U16(16, 2); U16(18, 1); U16(20, 6); U16(24, 1); U16(26, 3); U16(28, 0); U16(30, ushort.MaxValue); U16(32, 0);
        var original = source.ToArray();
        var a = new byte[128]; a[0] = 1; var b = new byte[128]; b[127] = 128;
        var masks = new Dictionary<(int, int, int), byte[]> { [(1, 2, 3)] = a, [(3, 2, 3)] = b };
        var output = UyaOcclusionGridWriter.ReplaceMasks(source, masks); var grid = UyaOcclusionGridReader.Read(output);
        Check(grid.Octants.Count == 2 && grid.Octants[0].MaskIndex != grid.Octants[1].MaskIndex, "shared original slot can split");
        Check(source.SequenceEqual(original) && grid.Octants.Select(c => c.X).SequenceEqual(new[] { 1, 3 }), "source and sparse octants preserved");
        var aliased = source.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(aliased.AsSpan(6), 2); BinaryPrimitives.WriteUInt16LittleEndian(aliased.AsSpan(10), 4);
        var aliasedMasks = new Dictionary<(int,int,int),byte[]> { [(1,2,3)] = a, [(3,2,3)] = a, [(1,2,4)] = b, [(3,2,4)] = b };
        var split = UyaOcclusionGridWriter.ReplaceMasks(aliased, aliasedMasks); var splitGrid = UyaOcclusionGridReader.Read(split);
        Check(splitGrid.Octants.Count == 4 && splitGrid.Octants.Where(c => c.Z == 3).All(c => split[splitGrid.MasksOffset+c.MaskIndex*128] == 1)
            && splitGrid.Octants.Where(c => c.Z == 4).All(c => split[splitGrid.MasksOffset+c.MaskIndex*128+127] == 128), "shared source tree rows can split");
        masks[(3, 2, 3)] = a; output = UyaOcclusionGridWriter.ReplaceMasks(source, masks); grid = UyaOcclusionGridReader.Read(output);
        Check(grid.Octants[0].MaskIndex == grid.Octants[1].MaskIndex, "deduplicates equal masks");
        Check(output.SequenceEqual(UyaOcclusionGridWriter.ReplaceMasks(source, masks)), "deterministic native output");
        Throws(() => UyaOcclusionGridWriter.ReplaceMasks(source, new Dictionary<(int,int,int),byte[]>()));
        masks[(3, 2, 3)] = new byte[127]; Throws(() => UyaOcclusionGridWriter.ReplaceMasks(source, masks));
        Console.WriteLine("Native full-grid mask replacement checks passed");
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
    private static void Throws(Action action) { try { action(); throw new Exception("Invalid masks accepted"); } catch (InvalidDataException) { } }
}
