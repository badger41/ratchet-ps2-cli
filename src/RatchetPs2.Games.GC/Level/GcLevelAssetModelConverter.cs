using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Skyboxes;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.GC.Level;

internal static class GcLevelAssetModelConverter
{
    public static byte[] Convert(FrontendAssetKind kind, byte[] bytes, int targetClassId, CancellationToken cancellationToken = default)
    {
        if (kind == FrontendAssetKind.Tie) return ConvertTie(bytes, targetClassId, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var result = bytes.ToArray();
        switch (kind)
        {
            case FrontendAssetKind.Moby:
                _ = MobyModelReader.Read(stream);
                break;
            case FrontendAssetKind.Shrub:
                _ = ShrubClassReader.Read(stream);
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x24), checked((ushort)targetClassId));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return result;
    }

    private static byte[] ConvertTie(byte[] bytes, int targetClassId, CancellationToken token, TieAnalysis? analysis = null)
    {
        var result = PatchTiePackets(bytes, bytes.ToArray(), analysis ?? new(bytes), token);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x44), checked((ushort)targetClassId));
        _ = TieClassReader.Read(result, TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        return result;
    }

    private static byte[] PatchTiePackets(byte[] bytes, byte[] result, TieAnalysis analysis,
        CancellationToken cancellationToken, GcTiePacketLayout layout = GcTiePacketLayout.Current)
    {
        var tie = analysis.Tie;
        foreach (var block in tie.PacketDataBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packet = tie.PacketTables[block.LodIndex].Packets[block.PacketIndex];
            if (packet.ShaderCount is < 1 or > 4)
                throw new InvalidDataException("GC tie packet shader count must be between one and four.");
            // UYA locates strip controls after five uploaded rows per shader and four setup rows; GC leaves this word zero.
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(packet.AbsoluteDataOffset + 0x0c),
                -(4 + 5 * packet.ShaderCount));
        }
        var facingByLod = new Dictionary<int, IReadOnlyList<TieTriangle>>();
        var partitionFacingByLod = new Dictionary<int, IReadOnlyList<TieTriangle>>();
        var mixedPackets = new HashSet<(int Lod, int Packet)>();
        foreach (var topology in tie.LodTopologies.Where(value => value.TriangleCount > 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Keep established boundaries and add splits where corrected facing or odd strips require them.
            var partitionFacing = analysis.Facing(topology.LodIndex, GcTiePacketLayout.PreviousFacing);
            var facing = analysis.Facing(topology.LodIndex, layout);
            facingByLod.Add(topology.LodIndex, facing);
            partitionFacingByLod.Add(topology.LodIndex, partitionFacing);
            foreach (var strip in topology.Strips)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var block = analysis.Blocks[(topology.LodIndex, strip.PacketIndex)];
                var packet = tie.PacketTables[topology.LodIndex].Packets[strip.PacketIndex];
                if (packet.BfcDistance == 0) continue;
                var phases = ResolveStripFacing(topology, strip, facing).Where(value => value.HasValue)
                    .Select(value => value!.Value).Distinct().ToArray();
                if (layout == GcTiePacketLayout.Current && block.StripControls[strip.PacketStripIndex].TokenCount % 2 != 0
                    || phases.Length > 1 || ResolveStripFacing(topology, strip, partitionFacing)
                    .Where(value => value.HasValue).Distinct().Count() > 1)
                {
                    mixedPackets.Add((topology.LodIndex, strip.PacketIndex));
                    continue;
                }
                var reverse = phases.FirstOrDefault() ^ ((strip.Flags & 0x20) != 0);
                var control = block.StripControls[strip.PacketStripIndex];
                // UYA's culling path compares the sign against strip-control .w bit 0x20; GC leaves it unused.
                result[control.Offset + 3] = (byte)((control.Flags & ~0x20) | (reverse == true ? 0x20 : 0));
            }
        }
        return mixedPackets.Count == 0 ? result : GcTiePacketRewriter.Rewrite(
            bytes, result, tie, facingByLod, partitionFacingByLod, mixedPackets, cancellationToken, layout);
    }

    internal static bool?[] ResolveStripFacing(TieLodTopology topology, TieTriangleStrip strip,
        IReadOnlyList<TieTriangle> facing)
    {
        var phases = new bool?[strip.TriangleCount];
        for (var offset = 0; offset < strip.TriangleCount; offset++)
        {
            var index = strip.TriangleStartIndex + offset;
            var original = topology.Triangles[index];
            TiePacketDecodedVertex Vertex(int index) => topology.LogicalVertices[index].DecodedVertex
                ?? throw new InvalidDataException("GC tie culling requires decoded vertex positions.");
            var a = Vertex(original.A); var b = Vertex(original.B); var c = Vertex(original.C);
            var ab = new System.Numerics.Vector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
            var ac = new System.Numerics.Vector3(c.X - a.X, c.Y - a.Y, c.Z - a.Z);
            if (System.Numerics.Vector3.Cross(ab, ac).LengthSquared() != 0)
                phases[offset] = facing[index].B == original.C;
        }
        return phases;
    }

    public static byte[]? RepairPreviousStaticConversion(FrontendAssetKind kind, byte[] source,
        byte[] converted, int previousClassId, int targetClassId, CancellationToken cancellationToken)
    {
        var classOffset = kind switch
        {
            FrontendAssetKind.Tie => 0x44,
            FrontendAssetKind.Shrub => 0x24,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var analysis = kind == FrontendAssetKind.Tie ? new TieAnalysis(source) : null;
        var corrected = analysis is null ? Convert(kind, source, targetClassId, cancellationToken)
            : ConvertTie(source, targetClassId, cancellationToken, analysis);
        if (corrected.AsSpan().SequenceEqual(converted)) return corrected;
        if (!MatchesClass(source, converted, classOffset, previousClassId)
            && !MatchesClass(corrected, converted, classOffset, previousClassId))
        {
            if (kind != FrontendAssetKind.Tie) return null;
            // Recognize the earlier split conversion exactly, without replacing independently edited models.
            var recognized = false;
            foreach (var previousLayout in new[] { GcTiePacketLayout.OriginalSplit, GcTiePacketLayout.MissingFatPhase, GcTiePacketLayout.PreviousFacing, GcTiePacketLayout.CoincidentFacing, GcTiePacketLayout.UnpaddedStrips })
            {
                var split = PatchTiePackets(source, source.ToArray(), analysis!, cancellationToken, previousLayout);
                if (MatchesClass(split, converted, classOffset, previousClassId)) { recognized = true; break; }
            }
            if (!recognized) return null;
        }
        return corrected;
    }

    private static bool MatchesClass(byte[] candidate, byte[] converted, int offset, int classId) =>
        candidate.Length == converted.Length
        && BinaryPrimitives.ReadUInt16LittleEndian(converted.AsSpan(offset)) == classId
        && candidate.AsSpan(0, offset).SequenceEqual(converted.AsSpan(0, offset))
        && candidate.AsSpan(offset + 2).SequenceEqual(converted.AsSpan(offset + 2));

    // A repair may try several historical writers; parse and resolve each facing policy only once.
    private sealed class TieAnalysis
    {
        private static readonly TieGameProfile Profile = TieGameProfile.ForGame(GameId.GC);
        private readonly Dictionary<(int Lod, GcTiePacketLayout Facing), IReadOnlyList<TieTriangle>> _facing = [];
        public TieClass Tie { get; }
        public IReadOnlyDictionary<(int Lod, int Packet), TiePacketDataBlock> Blocks { get; }

        public TieAnalysis(byte[] source)
        {
            Tie = TieClassReader.Read(source, TieClassReadOptions.ForGameProfile(Profile));
            Blocks = Tie.PacketDataBlocks.ToDictionary(block => (block.LodIndex, block.PacketIndex));
        }

        public IReadOnlyList<TieTriangle> Facing(int lod, GcTiePacketLayout layout)
        {
            var policy = layout switch
            {
                GcTiePacketLayout.Current or GcTiePacketLayout.UnpaddedStrips => GcTiePacketLayout.Current,
                GcTiePacketLayout.CoincidentFacing => GcTiePacketLayout.CoincidentFacing,
                _ => GcTiePacketLayout.PreviousFacing,
            };
            var key = (lod, policy);
            if (_facing.TryGetValue(key, out var value)) return value;
            value = TieGltfExporter.ResolveTriangleFacing(Tie, lod, Profile,
                includeCoincidentAdjacency: policy == GcTiePacketLayout.PreviousFacing,
                includeOverlappingAdjacency: policy == GcTiePacketLayout.CoincidentFacing);
            _facing.Add(key, value);
            return value;
        }
    }

    public static byte[] ConvertSky(byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes, writable: false);
        _ = SkyboxReader.Read(stream, GameId.GC);
        stream.Position = 0;
        _ = SkyboxReader.Read(stream, GameId.UYA);
        return bytes.ToArray();
    }
}
