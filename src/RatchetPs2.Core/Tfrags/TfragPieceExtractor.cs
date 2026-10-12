using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace RatchetPs2.Core.Tfrags;

public static class TfragPieceExtractor
{
    private const int MaximumPayloadCount = 4096;
    private const int MaximumPayloadBytes = 256 * 1024 * 1024;
    private const long MaximumTotalPayloadBytes = 512L * 1024 * 1024;
    private const int MaximumGroupCount = 100_000;
    private const int MaximumArchivePathLength = 1024;

    public static TfragPieceExtraction Extract(
        IReadOnlyList<TfragPiecePayload> payloads,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        if (payloads.Count > MaximumPayloadCount)
        {
            throw new InvalidDataException(
                $"Tfrag extraction contains {payloads.Count} payloads; the limit is {MaximumPayloadCount}.");
        }

        var sources = payloads
            .Select(ValidateAndNormalize)
            .OrderBy(payload => payload.SourceKind)
            .ThenBy(payload => payload.SourceIndex)
            .ThenBy(payload => payload.SourceArchive, StringComparer.Ordinal)
            .ToArray();
        var totalPayloadBytes = sources.Sum(source => (long)source.Bytes.Length);
        if (totalPayloadBytes > MaximumTotalPayloadBytes)
        {
            throw new InvalidDataException(
                $"Tfrag extraction contains {totalPayloadBytes} payload bytes; the limit is {MaximumTotalPayloadBytes} bytes.");
        }
        var duplicateSource = sources
            .GroupBy(payload => (payload.SourceKind, payload.SourceIndex))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSource is not null)
        {
            throw new InvalidDataException(
                $"Tfrag extraction contains duplicate {duplicateSource.Key.SourceKind} source index {duplicateSource.Key.SourceIndex}.");
        }

        var assets = new Dictionary<string, TfragPieceAsset>(StringComparer.Ordinal);
        var occurrences = new List<TfragPieceOccurrence>();
        var extractedSources = new List<TfragPieceSource>(sources.Length);
        var groupCount = 0;
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TfragTerrain terrain;
            try
            {
                terrain = TfragTerrainReader.Read(source.Bytes);
                groupCount = checked(groupCount + terrain.Chunks.Count);
                if (groupCount > MaximumGroupCount)
                {
                    throw new InvalidDataException(
                        $"Tfrag extraction contains more than {MaximumGroupCount} native groups.");
                }
                extractedSources.Add(new(
                    source.SourceArchive,
                    source.SourceKind,
                    source.SourceIndex,
                    terrain.Chunks.Count));
            }
            catch (Exception exception) when (exception is InvalidDataException or OverflowException or ArgumentException)
            {
                throw new InvalidDataException($"Tfrag payload '{source.SourceArchive}' " +
                    $"({DescribeSource(source)}) is invalid: {exception.Message}", exception);
            }

            foreach (var chunk in terrain.Chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var decoded = TfragGltfExporter.DecodeCanonicalPiece(terrain, chunk, cancellationToken);
                    var canonicalBytes = TfragPieceCanonicalCodec.Write(decoded);
                    var contentId = TfragPieceCanonicalCodec.ComputeContentId(canonicalBytes);
                    if (assets.TryGetValue(contentId, out var existing))
                    {
                        if (!existing.CanonicalBytes.AsSpan().SequenceEqual(canonicalBytes))
                        {
                            throw new InvalidDataException($"Canonical tfrag content hash collision for {contentId}.");
                        }
                    }
                    else
                    {
                        assets.Add(contentId, new TfragPieceAsset(
                            contentId,
                            TfragPieceCanonicalCodec.FormatVersion,
                            canonicalBytes,
                            decoded));
                    }

                    var provenance = new TfragPieceProvenance(
                        source.SourceArchive,
                        source.SourceKind,
                        source.SourceIndex,
                        chunk.Index,
                        chunk.OcclusionIndex,
                        chunk.OcclusionIndexStash);
                    var decodedVertexCount = decoded.Lods.Sum(lod => lod.Primitives.Sum(value => value.Vertices.Count));
                    var decodedTriangleCount = decoded.Lods.Sum(lod => lod.Primitives.Sum(value => value.Indices.Count / 3));
                    occurrences.Add(new TfragPieceOccurrence(
                        ComputeOccurrenceKey(provenance),
                        contentId,
                        provenance,
                        new TfragPieceTransform(
                            new(
                                CanonicalWorld(chunk.BoundingSphere.X / 1024f),
                                CanonicalWorld(chunk.BoundingSphere.Y / 1024f),
                                CanonicalWorld(chunk.BoundingSphere.Z / 1024f)),
                            new(0, 0, 0, 1),
                            new(1, 1, 1)),
                        new TfragPieceDiagnostics(
                            chunk.DataLength,
                            chunk.VertexCount,
                            chunk.TriangleCount,
                            decodedVertexCount,
                            decodedTriangleCount)));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is InvalidDataException or OverflowException or ArgumentException)
                {
                    throw new InvalidDataException($"Tfrag payload '{source.SourceArchive}' " +
                        $"({DescribeSource(source)}), group {chunk.Index} failed extraction: {exception.Message}", exception);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new TfragPieceExtraction(
            assets.Values.OrderBy(asset => asset.ContentId, StringComparer.Ordinal).ToArray(),
            occurrences,
            extractedSources);
    }

    private static TfragPiecePayload ValidateAndNormalize(TfragPiecePayload source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Bytes);
        if (!Enum.IsDefined(source.SourceKind))
        {
            throw new InvalidDataException($"Tfrag source kind {source.SourceKind} is invalid.");
        }

        if (source.SourceIndex < 0
            || source.SourceKind == TfragPieceSourceKind.Primary && source.SourceIndex != 0
            || source.SourceKind == TfragPieceSourceKind.Chunk && source.SourceIndex == 0)
        {
            throw new InvalidDataException(
                $"Tfrag {source.SourceKind} source index {source.SourceIndex} is invalid.");
        }

        if (source.Bytes.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"Tfrag payload '{source.SourceArchive}' is {source.Bytes.Length} bytes; the limit is {MaximumPayloadBytes} bytes.");
        }

        if (source.Bytes.Length >= 8
            && BinaryPrimitives.ReadInt32LittleEndian(source.Bytes.AsSpan(4, 4)) > MaximumGroupCount)
        {
            throw new InvalidDataException(
                $"Tfrag payload '{source.SourceArchive}' contains more than {MaximumGroupCount} native groups.");
        }

        return source with { SourceArchive = NormalizeArchivePath(source.SourceArchive) };
    }

    private static string NormalizeArchivePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = path.Replace('\\', '/');
        if (normalized.Length == 0
            || normalized.Length > MaximumArchivePathLength
            || normalized.Contains('\0')
            || normalized.StartsWith('/'))
        {
            throw new InvalidDataException("Tfrag source archive path is invalid.");
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..")
            || segments[0].Contains(':'))
        {
            throw new InvalidDataException($"Tfrag source archive path '{path}' must be project-relative.");
        }

        return string.Join('/', segments);
    }

    private static string ComputeOccurrenceKey(TfragPieceProvenance provenance)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("tfrag-occurrence-v0");
        writer.Write(provenance.SourceArchive);
        writer.Write((int)provenance.SourceKind);
        writer.Write(provenance.SourceIndex);
        writer.Write(provenance.GroupIndex);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))))
            .ToLowerInvariant();
    }

    private static float CanonicalWorld(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException("Tfrag bounding sphere contains a non-finite position.");
        }

        return value == 0 ? 0 : value;
    }

    private static string DescribeSource(TfragPiecePayload source) =>
        source.SourceKind == TfragPieceSourceKind.Primary
            ? "primary"
            : $"chunk {source.SourceIndex}";
}
