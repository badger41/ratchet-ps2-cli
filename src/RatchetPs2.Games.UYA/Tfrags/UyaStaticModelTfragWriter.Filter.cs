using System.Buffers.Binary;
using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static partial class UyaStaticModelTfragWriter
{
    public static byte[] Filter(
        byte[] sourceTerrainBytes,
        IReadOnlyList<int> retainedGroupIndices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTerrainBytes);
        ArgumentNullException.ThrowIfNull(retainedGroupIndices);
        var source = TfragTerrainReader.Read(sourceTerrainBytes);
        if (retainedGroupIndices.Any(value => value < 0 || value >= source.TfragCount)
            || retainedGroupIndices.Count != retainedGroupIndices.Distinct().Count())
            throw new ArgumentException("Retained UYA tfrag group indices are invalid.", nameof(retainedGroupIndices));
        var retained = retainedGroupIndices.Order().Select(index => source.Chunks[index]).ToArray();

        using var output = new MemoryStream();
        output.SetLength(checked(TerrainHeaderSize + retained.Length * RecordSize));
        sourceTerrainBytes.AsSpan(0, TerrainHeaderSize).CopyTo(output.GetBuffer());
        output.Position = output.Length;
        Align(output, 0x10);
        for (var index = 0; index < retained.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyChunk(output, index, sourceTerrainBytes, retained[index]);
        }

        var bytes = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes, TerrainHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), retained.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12),
            checked(source.TotalTfragCount - source.TfragCount + retained.Length));
        var filtered = TfragTerrainReader.Read(bytes);
        if (filtered.TfragCount != retained.Length)
            throw new InvalidDataException("Filtered UYA tfrag inventory failed verification.");
        for (var index = 0; index < retained.Length; index++)
            ValidateCopiedChunk(sourceTerrainBytes, retained[index], bytes, filtered.Chunks[index]);
        return bytes;
    }
}
