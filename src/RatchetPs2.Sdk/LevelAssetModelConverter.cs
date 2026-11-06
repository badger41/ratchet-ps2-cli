using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures.Palettes;
using RatchetPs2.Games.DL.Moby;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Games.UYA.Moby;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Sdk;

public static class LevelAssetModelConverter
{
    public static bool MatchesTextureBaseUpdate(GameId sourceGame, IReadOnlyList<FrontendAssetTexture> original,
        IReadOnlyList<FrontendAssetTexture> updated, CancellationToken token = default) => sourceGame switch
    {
        GameId.RC1 => Rc1TextureConversionRepair.MatchesBase(original, updated, token),
        _ => throw new NotSupportedException($"Texture base updates are not supported for {sourceGame}."),
    };

    public static IReadOnlyList<FrontendAssetTexture>? RepairTextureUpdate(GameId sourceGame, GameId targetGame,
        IReadOnlyList<FrontendAssetTexture> original, IReadOnlyList<FrontendAssetTexture> updated,
        IReadOnlyList<FrontendAssetTexture> native, CancellationToken token = default) => (sourceGame, targetGame) switch
    {
        (GameId.RC1, GameId.UYA) => Rc1TextureConversionRepair.RepairToUya(original, updated, native, token),
        _ => throw new NotSupportedException($"Texture update repair from {sourceGame} to {targetGame} is not supported."),
    };

    public static byte[]? RepairPreviousMobyConversion(GameId sourceGame, GameId targetGame, byte[] sourceBytes,
        byte[] convertedBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(convertedBytes);
        if (sourceGame != GameId.DL || targetGame != GameId.UYA)
            throw new NotSupportedException($"Moby conversion repair from {sourceGame} to {targetGame} is not supported.");
        var corrected = ConvertMoby(sourceBytes, cancellationToken);
        if (corrected.AsSpan().SequenceEqual(convertedBytes)) return null;
        bool MatchesPrevious(byte[] expected) => expected.AsSpan().SequenceEqual(convertedBytes)
            || UyaMobyConverter.MatchesPreviousLodJointCount(sourceBytes, expected, convertedBytes);
        if (!MatchesPrevious(corrected)
            && !MatchesPrevious(ConvertMoby(sourceBytes, cancellationToken, rebuildSkeletonFallback: false))
            && !MatchesPrevious(ConvertMoby(sourceBytes, cancellationToken, normalizeRuntimeMetadata: false))
            && !MatchesPrevious(ConvertMoby(sourceBytes, cancellationToken, preserveSounds: false, normalizeRuntimeMetadata: false)))
            return null;
        return corrected;
    }

    public static byte[]? RepairPreviousTextureConversion(GameId sourceGame, GameId targetGame,
        byte[] sourceBytes, byte[] convertedBytes, TextureRole role = TextureRole.Material)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(convertedBytes);
        if (sourceGame != GameId.DL || targetGame != GameId.UYA)
            throw new NotSupportedException($"Texture conversion repair from {sourceGame} to {targetGame} is not supported.");
        return UyaLevelAssetModelConverter.RepairPreviousDlTexture(sourceBytes, convertedBytes, role);
    }

    public static byte[]? RepairPreviousTieConversion(GameId sourceGame, GameId targetGame,
        byte[] sourceBytes, byte[] convertedBytes, int targetClassId)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(convertedBytes);
        if (targetClassId is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(targetClassId));
        if (sourceGame != GameId.DL || targetGame != GameId.UYA)
            throw new NotSupportedException($"Tie conversion repair from {sourceGame} to {targetGame} is not supported.");
        return UyaLevelAssetModelConverter.RepairPreviousDlTie(sourceBytes, convertedBytes, targetClassId);
    }

    /// <summary>Returns the corrected model for a recognized conversion (including unchanged models), or null for independent edits.</summary>
    public static byte[]? RepairPreviousStaticConversion(GameId sourceGame, GameId targetGame,
        FrontendAssetKind kind, byte[] sourceBytes, byte[] convertedBytes, int previousClassId, int targetClassId,
        CancellationToken cancellationToken = default, IReadOnlyList<FrontendAssetTexture>? textures = null)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(convertedBytes);
        if (previousClassId is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(previousClassId));
        if (targetClassId is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(targetClassId));
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceGame == GameId.RC1 && targetGame == GameId.UYA && kind == FrontendAssetKind.Tie)
            return Rc1LevelAssetModelConverter.RepairPreviousTie(sourceBytes, convertedBytes,
                previousClassId, targetClassId, textures, cancellationToken);
        if (sourceGame != GameId.GC || targetGame != GameId.UYA)
            throw new NotSupportedException($"Static conversion repair from {sourceGame} to {targetGame} is not supported.");
        return GcLevelAssetModelConverter.RepairPreviousStaticConversion(kind, sourceBytes, convertedBytes,
            previousClassId, targetClassId, cancellationToken);
    }

    public static byte[] ConvertTexture(GameId sourceGame, GameId targetGame, byte[] pifBytes,
        TextureRole role = TextureRole.Material)
    {
        ArgumentNullException.ThrowIfNull(pifBytes);
        if ((sourceGame is GameId.GC or GameId.RC1) && targetGame == GameId.UYA)
            return UyaLevelAssetModelConverter.ConvertLinearTexture(pifBytes, role);
        if (sourceGame != GameId.DL || targetGame != GameId.UYA)
            throw new NotSupportedException($"Texture conversion from {sourceGame} to {targetGame} is not supported.");
        return UyaLevelAssetModelConverter.ConvertDlTexture(pifBytes, role);
    }

    public static byte[] Convert(GameId sourceGame, GameId targetGame, FrontendAssetKind kind,
        byte[] modelBytes, int targetClassId, CancellationToken cancellationToken = default,
        IReadOnlyList<FrontendAssetTexture>? textures = null)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        if (targetClassId is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(targetClassId));
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceGame == GameId.RC1 && targetGame == GameId.UYA)
            return Rc1LevelAssetModelConverter.Convert(kind, modelBytes, targetClassId, cancellationToken, textures);
        if (sourceGame == GameId.GC && targetGame == GameId.UYA)
            return GcLevelAssetModelConverter.Convert(kind, modelBytes, targetClassId, cancellationToken);
        if (sourceGame != GameId.DL || targetGame != GameId.UYA)
            throw new NotSupportedException($"Model conversion from {sourceGame} to {targetGame} is not supported.");
        return kind switch
        {
            FrontendAssetKind.Tie => UyaLevelAssetModelConverter.ConvertDlTie(modelBytes, targetClassId),
            FrontendAssetKind.Shrub => UyaLevelAssetModelConverter.ConvertDlShrub(modelBytes, targetClassId),
            FrontendAssetKind.Moby => ConvertMoby(modelBytes, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>Splits oversized models without dropping geometry or LODs. Each part initially retains targetClassId.</summary>
    public static IReadOnlyList<byte[]> ConvertParts(GameId sourceGame, GameId targetGame, FrontendAssetKind kind,
        byte[] modelBytes, int targetClassId, CancellationToken cancellationToken = default,
        IReadOnlyList<FrontendAssetTexture>? textures = null)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        if (targetClassId is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(targetClassId));
        cancellationToken.ThrowIfCancellationRequested();
        return sourceGame == GameId.RC1 && targetGame == GameId.UYA
            ? Rc1LevelAssetModelConverter.ConvertParts(kind, modelBytes, targetClassId, cancellationToken, textures)
            : [Convert(sourceGame, targetGame, kind, modelBytes, targetClassId, cancellationToken, textures)];
    }

    public static byte[] RebaseClass(GameId game, FrontendAssetKind kind, byte[] modelBytes, int targetClassId)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        if (targetClassId is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(targetClassId));
        if (game != GameId.UYA || kind != FrontendAssetKind.Tie)
            throw new NotSupportedException($"Model class rebasing is not supported for {game} {kind}.");
        return UyaLevelAssetModelConverter.RebaseTieClass(modelBytes, targetClassId);
    }

    private static byte[] ConvertMoby(byte[] bytes, CancellationToken cancellationToken, bool preserveSounds = true,
        bool normalizeRuntimeMetadata = true, bool rebuildSkeletonFallback = true)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var model = MobyModelReader.Read(stream, new() { AnimationFormat = MobyAnimationFormat.Compact });
        if (!float.IsFinite(model.Scale)) throw new InvalidDataException("Model scale is invalid.");
        if (Math.Abs(model.Scale) < 1e-8f) model.Scale = 1;
        var animations = new List<MobyGltfAnimationClip>();
        for (var index = 0; index < model.Sequences.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DlCompactAnimationDecoder.TryDecode(model.Sequences[index], index, model.JointCount,
                model.Scale, out var clip, out var error))
                throw new InvalidDataException($"DL animation {index} cannot be converted: {error}.");
            animations.Add(clip);
        }
        return UyaMobyConverter.ConvertFromCompact(model, animations, cancellationToken, preserveSounds,
            normalizeRuntimeMetadata, rebuildSkeletonFallback);
    }
}
