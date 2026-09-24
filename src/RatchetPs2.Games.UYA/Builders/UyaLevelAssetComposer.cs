using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaLevelAssetComposer
{
    private const int AssetAlignment = 0x10;
    private const int ChunkHeaderSize = 0x10;

    public static LevelAssetWadComposition ComposeAssetWad(
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> assetWadBytes,
        LevelAssetWadPayloads replacements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        var header = DlAssetReader.ReadHeader(headerBytes);
        var mobys = DlAssetReader.ReadModelDefinitions(
            headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = DlAssetReader.ReadModelDefinitions(
            headerBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = DlAssetReader.ReadShrubDefinitions(
            headerBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var references = CollectReferences(header, mobys, ties, shrubs, assetWadBytes.Length);
        var offsets = references.Select(value => value.AssetOffset)
            .Append(assetWadBytes.Length).Distinct().Order().ToArray();
        if (offsets.Length < 2)
            throw new InvalidDataException("UYA asset WAD has no addressable payloads.");

        var replacementByOffset = new Dictionary<int, ReadOnlyMemory<byte>>();
        AddReplacement(replacementByOffset, "terrain", header.TerrainOffset, replacements.Terrain, assetWadBytes.Length);
        AddReplacement(replacementByOffset, "sky", header.SkyOffset, replacements.Sky, assetWadBytes.Length);
        AddReplacement(replacementByOffset, "collision", header.CollisionOffset, replacements.Collision, assetWadBytes.Length);
        if (replacementByOffset.Count == 0)
            return new(headerBytes.ToArray(), assetWadBytes.ToArray());

        using var output = new MemoryStream(assetWadBytes.Length);
        output.Write(assetWadBytes[..offsets[0]]);
        var relocated = new Dictionary<int, int>();
        for (var index = 0; index < offsets.Length - 1; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceOffset = offsets[index];
            var sourceEnd = offsets[index + 1];
            Align(output, AssetAlignment);
            relocated.Add(sourceOffset, checked((int)output.Position));
            if (replacementByOffset.TryGetValue(sourceOffset, out var replacement))
                output.Write(replacement.Span);
            else
                output.Write(assetWadBytes[sourceOffset..sourceEnd]);
        }

        var composedHeader = headerBytes.ToArray();
        foreach (var reference in references)
            BinaryPrimitives.WriteInt32LittleEndian(
                composedHeader.AsSpan(reference.HeaderOffset, sizeof(int)),
                relocated[reference.AssetOffset]);
        var composedAsset = output.ToArray();
        ValidateComposition(composedHeader, composedAsset, replacements);
        return new(composedHeader, composedAsset);
    }

    public static byte[] ComposeTfragChunk(
        ReadOnlySpan<byte> chunkBytes,
        ReadOnlySpan<byte> terrainBytes,
        WadDecompressionOptions? decompression = null,
        CancellationToken cancellationToken = default)
    {
        if (terrainBytes.IsEmpty) throw new ArgumentException("Tfrag terrain payload cannot be empty.", nameof(terrainBytes));
        if (chunkBytes.Length < ChunkHeaderSize) throw new InvalidDataException("Tfrag chunk is shorter than its header.");
        var payloadOffset = ReadOffset(chunkBytes, 0);
        var payloadEnd = ReadOffset(chunkBytes, sizeof(int));
        if (payloadOffset < ChunkHeaderSize || payloadOffset >= chunkBytes.Length)
            throw new InvalidDataException("Tfrag chunk terrain offset is outside the chunk.");
        if (payloadEnd <= payloadOffset || payloadEnd > chunkBytes.Length) payloadEnd = chunkBytes.Length;

        var wasCompressed = BinaryMagic.IsWad(chunkBytes[payloadOffset..payloadEnd]);
        var encoded = wasCompressed
            ? WadCompression.CompressVerified(terrainBytes, decompression ?? new(), cancellationToken).CompressedBytes
            : terrainBytes.ToArray();
        var newPayloadEnd = wasCompressed
            ? Align(payloadOffset + encoded.Length, AssetAlignment)
            : checked(payloadOffset + encoded.Length);
        var output = new byte[checked(newPayloadEnd + chunkBytes.Length - payloadEnd)];
        chunkBytes[..payloadOffset].CopyTo(output);
        encoded.CopyTo(output.AsSpan(payloadOffset));
        chunkBytes[payloadEnd..].CopyTo(output.AsSpan(newPayloadEnd));
        var delta = newPayloadEnd - payloadEnd;
        for (var offset = 0; offset < ChunkHeaderSize; offset += sizeof(int))
        {
            var value = ReadOffset(chunkBytes, offset);
            if (value >= payloadEnd)
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset, sizeof(int)), checked(value + delta));
        }
        if (!TfragChunkWadReader.ReadTerrainPayload(output).AsSpan().SequenceEqual(terrainBytes))
            throw new InvalidDataException("Composed tfrag chunk failed semantic verification.");
        return output;
    }

    private static IReadOnlyList<OffsetReference> CollectReferences(
        DlAssetHeader header,
        IReadOnlyList<DlAssetModelDefinition> mobys,
        IReadOnlyList<DlAssetModelDefinition> ties,
        IReadOnlyList<DlAssetShrubDefinition> shrubs,
        int assetLength)
    {
        var references = new List<OffsetReference>();
        AddDirect(references, 0x08, header.TerrainOffset, assetLength);
        AddDirect(references, 0x0c, header.OcclusionOffset, assetLength);
        AddDirect(references, 0x10, header.SkyOffset, assetLength);
        AddDirect(references, 0x14, header.CollisionOffset, assetLength);
        AddDirect(references, 0x60, header.TextureDataOffset, assetLength);
        AddDirect(references, 0x64, header.ParticleTextureDataOffset, assetLength);
        AddDirect(references, 0x68, header.FxTextureDataOffset, assetLength);
        AddDirect(references, 0xa4, header.HeightmapOffset, assetLength);
        AddDirect(references, 0xa8, header.OcclusionOctreeOffset, assetLength);
        AddDirect(references, 0xb0, header.OcclusionRadiusOffset, assetLength);
        AddDirect(references, 0xb8, header.OcclusionRadius2Offset, assetLength);
        AddModels(references, header.MobyModelOffset, 0x20, mobys.Select(value => value.ModelOffset), assetLength);
        AddModels(references, header.TieModelOffset, 0x20, ties.Select(value => value.ModelOffset), assetLength);
        AddModels(references, header.ShrubModelOffset, 0x30, shrubs.Select(value => value.ModelOffset), assetLength);
        return references;
    }

    private static void AddDirect(ICollection<OffsetReference> references, int headerOffset, int assetOffset, int assetLength)
    {
        if (assetOffset > 0 && assetOffset < assetLength) references.Add(new(headerOffset, assetOffset));
    }

    private static void AddModels(
        ICollection<OffsetReference> references,
        int tableOffset,
        int stride,
        IEnumerable<int> offsets,
        int assetLength)
    {
        var index = 0;
        foreach (var assetOffset in offsets)
        {
            if (assetOffset > 0 && assetOffset < assetLength)
                references.Add(new(checked(tableOffset + index * stride), assetOffset));
            index++;
        }
    }

    private static void AddReplacement(
        IDictionary<int, ReadOnlyMemory<byte>> replacements,
        string name,
        int offset,
        ReadOnlyMemory<byte>? bytes,
        int assetLength)
    {
        if (bytes is null) return;
        if (bytes.Value.IsEmpty) throw new ArgumentException($"UYA {name} replacement cannot be empty.", nameof(bytes));
        if (offset <= 0 || offset >= assetLength)
            throw new InvalidDataException($"UYA asset header has no writable {name} payload.");
        if (!replacements.TryAdd(offset, bytes.Value)
            && !replacements[offset].Span.SequenceEqual(bytes.Value.Span))
            throw new InvalidDataException($"UYA {name} payload aliases another replacement with different bytes.");
    }

    private static void ValidateComposition(
        byte[] headerBytes,
        byte[] assetBytes,
        LevelAssetWadPayloads replacements)
    {
        var header = DlAssetReader.ReadHeader(headerBytes);
        var mobys = DlAssetReader.ReadModelDefinitions(headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = DlAssetReader.ReadModelDefinitions(headerBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = DlAssetReader.ReadShrubDefinitions(headerBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var offsets = DlAssetReader.CollectKnownAssetOffsets(GameId.UYA, header, assetBytes.Length, mobys, ties, shrubs);
        Verify("terrain", replacements.Terrain, header.TerrainOffset, assetBytes, offsets);
        Verify("sky", replacements.Sky, header.SkyOffset, assetBytes, offsets);
        Verify("collision", replacements.Collision, header.CollisionOffset, assetBytes, offsets);
    }

    private static void Verify(
        string name,
        ReadOnlyMemory<byte>? expected,
        int offset,
        byte[] assetBytes,
        IReadOnlyList<int> offsets)
    {
        if (expected is null) return;
        var actual = DlAssetReader.ReadAssetSlice(assetBytes, offset, offsets);
        if (actual.Length < expected.Value.Length
            || !actual.AsSpan(0, expected.Value.Length).SequenceEqual(expected.Value.Span)
            || actual.AsSpan(expected.Value.Length).ContainsAnyExcept((byte)0))
            throw new InvalidDataException($"Composed UYA {name} payload failed semantic verification.");
    }

    private static void Align(Stream stream, int alignment)
    {
        var padding = Align(checked((int)stream.Position), alignment) - stream.Position;
        if (padding > 0) stream.Write(new byte[padding]);
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static int ReadOffset(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(int)));

    private readonly record struct OffsetReference(int HeaderOffset, int AssetOffset);
}
