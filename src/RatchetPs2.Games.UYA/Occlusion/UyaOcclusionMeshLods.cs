namespace RatchetPs2.Games.UYA.Occlusion;

/// <summary>Validates the native mesh inventory and selects LOD0 for occlusion.</summary>
public static class UyaOcclusionMeshLods
{
    public static int SelectLod0(IEnumerable<(int Index, int Faces)> available)
    {
        var lods = available.ToArray();
        if (lods.Length == 0 || lods.Any(lod => lod.Index < 0 || lod.Faces < 0)
            || lods.Select(lod => lod.Index).Distinct().Count() != lods.Length || !lods.Any(lod => lod.Index == 0))
            throw new InvalidDataException("Expected unique nonnegative mesh LODs including LOD0.");
        return 0;
    }
}
