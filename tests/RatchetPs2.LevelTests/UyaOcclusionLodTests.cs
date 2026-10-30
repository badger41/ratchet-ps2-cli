using RatchetPs2.Games.UYA.Occlusion;

internal static class UyaOcclusionLodTests
{
    public static void Run()
    {
        void Check(bool value) { if (!value) throw new Exception("Mesh LOD selection failed."); }
        Check(UyaOcclusionMeshLods.SelectLod0([(0, 100), (1, 30), (2, 10)]) == 0);
        Check(UyaOcclusionMeshLods.SelectLod0([(2, 10), (0, 100)]) == 0);
        Check(UyaOcclusionMeshLods.SelectLod0([(0, 0), (1, 10)]) == 0);
        Check(UyaOcclusionMeshLods.SelectLod0([(0, 0), (1, 0)]) == 0);
        Check(UyaOcclusionMeshLods.SelectLod0([(0, 10)]) == 0);
        foreach (var invalid in new (int, int)[][] { [], [(1, 1)], [(0, 1), (0, 2)], [(0, -1)], [(0, 1), (-1, 1)] })
        {
            try { UyaOcclusionMeshLods.SelectLod0(invalid); throw new Exception("Invalid LODs accepted."); }
            catch (InvalidDataException) { }
        }
    }
}
