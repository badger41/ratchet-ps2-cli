using RatchetPs2.Core.LevelAssets;

namespace RatchetPs2.Games.RC1.Level;

internal static partial class Rc1TieModelConverter
{
    public static IReadOnlyList<byte[]> ConvertParts(byte[] bytes, int classId, CancellationToken token,
        IReadOnlyList<FrontendAssetTexture>? textures)
    {
        var analysis = new Analysis(bytes);
        var maximum = analysis.Tie.PacketTables.Where(table => table.Packets.Count > 0)
            .Select(table => table.Packets.Count).DefaultIfEmpty(1).Min();
        for (var count = 1; count <= maximum; count++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return Enumerable.Range(0, count).Select(index => Convert(bytes, classId, token,
                    textures, analysis, partIndex: index, partCount: count)).ToArray();
            }
            catch (Rc1TieCapacityException) when (count < maximum) { }
        }
        throw new InvalidDataException("RC1 tie cannot be split within UYA's model limits.");
    }
}

internal sealed class Rc1TieCapacityException(string message) : NotSupportedException(message);
