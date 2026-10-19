using RatchetPs2.Games.UYA.Geometry;

namespace RatchetPs2.Games.UYA.Tfrags;

internal sealed record UyaTfragPacketPrimitive(
    int MaterialSlot,
    int VertexCount,
    uint[] Indices);

internal sealed record UyaTfragPacketPlan(
    IReadOnlyList<IReadOnlyList<UyaTriangleStrip>> StripsByPrimitive,
    int VertexCount,
    int StripIndexCount,
    int StripRowCount,
    int EndAddress)
{
    public bool FitsVuBuffer => EndAddress <= UyaTfragPacketPlanner.VuBufferQwords;
}

internal static class UyaTfragPacketPlanner
{
    public const int VuBufferQwords = 0x148;
    public const int MaximumVerticesPerGsPacket = 24;

    public static UyaTfragPacketPlan Build(
        IReadOnlyList<UyaTfragPacketPrimitive> primitives,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        var stripsByPrimitive = primitives.Select(primitive =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (primitive.VertexCount <= 0
                || primitive.Indices.Length == 0
                || primitive.Indices.Length % 3 != 0
                || primitive.Indices.Any(index => index >= primitive.VertexCount))
                throw new InvalidDataException("UYA tfrag packet planning received invalid primitive topology.");
            return UyaTriangleStripPlanner.Build(
                primitive.Indices,
                cancellationToken,
                UyaTriangleStripPlanner.MaximumStripVertices);
        }).ToArray();
        var vertexCount = AlignFour(primitives.Sum(value => value.VertexCount));
        var stripIndexCount = stripsByPrimitive.SelectMany(value => value).Sum(value => value.Vertices.Count);
        var stripRowCount = stripsByPrimitive.SelectMany(value => value).Count() + 1;
        var materialCount = primitives.Select(value => value.MaterialSlot).Distinct().Count();
        var endAddress = checked(10
            + materialCount * 5
            + vertexCount * 3
            + (stripIndexCount + 3) / 4
            + stripRowCount - 1);
        return new(stripsByPrimitive, vertexCount, stripIndexCount, stripRowCount, endAddress);
    }

    private static int AlignFour(int value) => checked((value + 3) & ~3);
}
