using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Sdk;

internal static class FrontendMapPackage
{
    public static PackedFilePackage PackWithGameplay(
        IEnumerable<PackedFile> renderFiles,
        IEnumerable<PackedFile> sourceFiles) =>
        PackedFilePackageBuilder.Pack(renderFiles.Concat(sourceFiles.Where(IsGameplayMetadata)).ToArray());

    private static bool IsGameplayMetadata(PackedFile file) =>
        file.Path == "gameplay/gameplay_core.bin"
        || file.Path.StartsWith("gameplay/core/", StringComparison.Ordinal);
}
