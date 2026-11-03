using RatchetPs2.Core.LevelAssets;

namespace RatchetPs2.Games.DL.Level;

public static class DlLevelAssetCanonicalizer
{
    public static byte[] NormalizeDefinition(ReadOnlySpan<byte> definitionBytes)
    {
        ValidateDefinition(definitionBytes);
        var definition = definitionBytes.ToArray();
        definition.AsSpan(0, 8).Clear();
        definition.AsSpan(0x10).Fill(byte.MaxValue);
        return definition;
    }

    public static void Validate(
        ReadOnlySpan<byte> definitionBytes,
        ReadOnlySpan<byte> modelBytes,
        IReadOnlyList<FrontendAssetTexture> textures)
    {
        ValidateDefinition(definitionBytes);
        if (modelBytes.IsEmpty) throw new InvalidDataException("Canonical DL model cannot be empty.");
        ArgumentNullException.ThrowIfNull(textures);
        if (textures.Count > 4_096 || textures.Any(value => value is null || value.Role > 1 || value.PifBytes.Length == 0))
            throw new InvalidDataException("Canonical DL textures are invalid.");
    }

    private static void ValidateDefinition(ReadOnlySpan<byte> definitionBytes)
    {
        if (definitionBytes.Length is not 0x20 and not 0x30)
            throw new InvalidDataException("Canonical DL definition must be 0x20 or 0x30 bytes.");
    }
}
