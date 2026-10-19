using System.Buffers.Binary;
using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static partial class UyaStaticModelTfragWriter
{
    public static byte[] Append(
        byte[] sourceTerrainBytes,
        IReadOnlyList<StaticModelTfragPiece> pieces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTerrainBytes);
        ArgumentNullException.ThrowIfNull(pieces);
        cancellationToken.ThrowIfCancellationRequested();
        if (pieces.Count == 0) return sourceTerrainBytes.ToArray();

        var source = TfragTerrainReader.Read(sourceTerrainBytes);
        if (source.TfragTableOffset != TerrainHeaderSize)
            throw new InvalidDataException(
                $"UYA tfrag append requires a 0x{TerrainHeaderSize:X} table offset; source uses 0x{source.TfragTableOffset:X}.");

        var generatedBytes = Write(pieces, cancellationToken);
        var generated = TfragTerrainReader.Read(generatedBytes);
        var combinedCount = checked(source.TfragCount + generated.TfragCount);
        using var output = new MemoryStream();
        output.SetLength(checked(TerrainHeaderSize + combinedCount * RecordSize));
        sourceTerrainBytes.AsSpan(0, TerrainHeaderSize).CopyTo(output.GetBuffer());
        output.Position = output.Length;
        Align(output, 0x10);

        var outputIndex = 0;
        foreach (var chunk in source.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyChunk(output, outputIndex++, sourceTerrainBytes, chunk);
        }
        foreach (var chunk in generated.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyChunk(output, outputIndex++, generatedBytes, chunk);
        }

        var bytes = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes, TerrainHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), combinedCount);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), MathF.Max(source.TfragRadius, generated.TfragRadius));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12),
            checked(source.TotalTfragCount + generated.TfragCount));
        ValidateAppend(bytes, sourceTerrainBytes, source, generatedBytes, generated, cancellationToken);
        return bytes;
    }

    private static void CopyChunk(
        MemoryStream output,
        int outputIndex,
        byte[] sourceBytes,
        TfragChunk chunk)
    {
        var dataOffset = checked((int)output.Position);
        output.Write(sourceBytes.AsSpan(chunk.DataOffset, chunk.DataLength));
        Align(output, 0x10);
        var record = output.GetBuffer().AsSpan(TerrainHeaderSize + outputIndex * RecordSize, RecordSize);
        sourceBytes.AsSpan(chunk.RecordOffset, RecordSize).CopyTo(record);
        BinaryPrimitives.WriteInt32LittleEndian(record[0x10..], dataOffset - TerrainHeaderSize);
    }

    private static void ValidateAppend(
        byte[] appendedBytes,
        byte[] sourceBytes,
        TfragTerrain source,
        byte[] generatedBytes,
        TfragTerrain generated,
        CancellationToken cancellationToken)
    {
        var appended = TfragTerrainReader.Read(appendedBytes);
        if (appended.TfragCount != source.TfragCount + generated.TfragCount)
            throw new InvalidDataException("Appended tfrag group inventory did not re-read.");

        for (var index = 0; index < source.TfragCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateCopiedChunk(sourceBytes, source.Chunks[index], appendedBytes, appended.Chunks[index]);
        }
        for (var index = 0; index < generated.TfragCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateCopiedChunk(generatedBytes, generated.Chunks[index], appendedBytes,
                appended.Chunks[source.TfragCount + index]);
        }
    }

    private static void ValidateCopiedChunk(
        byte[] expectedBytes,
        TfragChunk expected,
        byte[] actualBytes,
        TfragChunk actual)
    {
        if (expected.DataLength != actual.DataLength
            || !expectedBytes.AsSpan(expected.DataOffset, expected.DataLength)
                .SequenceEqual(actualBytes.AsSpan(actual.DataOffset, actual.DataLength)))
            throw new InvalidDataException($"Appended tfrag group {actual.Index} changed its native payload.");

        var expectedRecord = expectedBytes.AsSpan(expected.RecordOffset, RecordSize);
        var actualRecord = actualBytes.AsSpan(actual.RecordOffset, RecordSize);
        if (!expectedRecord[..0x10].SequenceEqual(actualRecord[..0x10])
            || !expectedRecord[0x14..].SequenceEqual(actualRecord[0x14..]))
            throw new InvalidDataException($"Appended tfrag group {actual.Index} changed its native record.");
    }
}
