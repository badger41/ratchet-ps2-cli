using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Sdk;

public static class GameplayInstanceConverter
{
    public static byte[] RemapMobyForImport(GameId game, ReadOnlySpan<byte> source, int expectedClassId,
        int uid, int targetClassId, int group) => game switch
    {
        GameId.UYA => UyaMobyInstancesWriter.RemapForImport(source, expectedClassId, uid, targetClassId, group),
        _ => throw new NotSupportedException($"Moby instance remapping is not supported for {game}."),
    };

    public static byte[] CreateGrindPathRecord(GameId game, int unknown4, int wrap, int inactive) => game switch
    {
        GameId.UYA => UyaGrindPathInstancesWriter.CreateRecord(unknown4, wrap, inactive),
        _ => throw new NotSupportedException($"Grind path records are not supported for {game}."),
    };

    public static byte[] RebaseStaticClass(GameId game, FrontendAssetKind kind, ReadOnlySpan<byte> source,
        int expectedClassId, int targetClassId) => (game, kind) switch
    {
        (GameId.UYA, FrontendAssetKind.Tie) => UyaTieInstancesWriter.RebaseClass(source, expectedClassId, targetClassId),
        (GameId.UYA, FrontendAssetKind.Shrub) => UyaShrubInstancesWriter.RebaseClass(source, expectedClassId, targetClassId),
        _ => throw new NotSupportedException($"{kind} instance class rebasing is not supported for {game}."),
    };

    public static byte[] RebaseMobyClass(GameId game, ReadOnlySpan<byte> source, int expectedClassId, int targetClassId) => game switch
    {
        GameId.UYA => UyaMobyInstancesWriter.RebaseClass(source, expectedClassId, targetClassId),
        _ => throw new NotSupportedException($"Moby instance class rebasing is not supported for {game}."),
    };
}
