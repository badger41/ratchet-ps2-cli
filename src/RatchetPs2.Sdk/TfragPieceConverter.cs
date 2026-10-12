using RatchetPs2.Core.Games;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class TfragPieceConverter
{
    public const int CompositionVersion = 4;

    public static TfragPieceExtraction Extract(
        IReadOnlyList<TfragPiecePayload> payloads,
        GameId gameId,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => TfragPieceExtractor.Extract(payloads, cancellationToken),
        _ => throw new NotSupportedException($"Tfrag piece extraction does not support {gameId}."),
    };

    public static void ValidateTransform(TfragPieceTransform transform) =>
        TfragPieceTransformer.Validate(transform);

    public static void ValidateTransform(TfragPieceBounds bounds, TfragPieceTransform transform) =>
        TfragPieceTransformer.Validate(bounds, transform);

    public static TfragPieceGeometry Transform(
        TfragPieceGeometry geometry,
        TfragPieceTransform transform,
        CancellationToken cancellationToken = default) =>
        TfragPieceTransformer.Transform(geometry, transform, cancellationToken);

    public static TfragPieceComposition Compose(
        byte[] sourceBytes,
        GameId gameId,
        IReadOnlyList<TfragPieceCompositionEdit> edits,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaTfragTerrainComposer.Compose(sourceBytes, edits, cancellationToken),
        _ => throw new NotSupportedException($"Tfrag piece composition does not support {gameId}."),
    };
}
