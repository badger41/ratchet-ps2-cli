using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.RC1.Ties;

public static class Rc1TieGameProfile
{
    public static TieGameProfile Default { get; } = TieGameProfile.Default with
    {
        GameLabel = "RC1",
        UsePacketRowSourceNormals = false,
        UseAmbientIndexAttribute = true,
        AmbientNormalIndexOffset = 0,
        AmbientWordCount = 64,
        UseNearestAmbientNormalFallback = false,
        UseExactVertexNormalTableRemaps = true,
        OrientTriangleWindingToNormals = true,
        UseStaticBackfaceCulling = true,
        UseDoubleSidedAlphaMaterials = true,
        VertexNormalHeaderSize = 0
    };
}
