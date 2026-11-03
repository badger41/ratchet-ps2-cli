using RatchetPs2.Core.Games;
using RatchetPs2.Core.Skyboxes;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Sdk;

public static class SkyboxPreviewPackageBuilder
{
    public static PackedFilePackage Build(GameId gameId, byte[] bytes, int? sourceLevel = null,
        int? shellIndex = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes, writable: false);
        var sky = SkyboxReader.Read(stream, gameId);
        IReadOnlyList<SkyboxShell> shells = shellIndex is { } index
            ? [sky.Shells.SingleOrDefault(value => value.Index == index)
                ?? throw new InvalidDataException($"Sky shell {index} is not present in the asset.")]
            : sky.Shells;
        var textureIds = shells.SelectMany(shell => shell.Clusters).SelectMany(cluster => cluster.Triangles)
            .Select(triangle => (int)triangle.TextureId).ToHashSet();
        var textures = sky.Textures.Where(texture => textureIds.Contains(texture.Index)).ToArray();
        var selected = new Skybox(
            sky.Header with
            {
                ShellCount = checked((short)shells.Count),
                SpriteCount = 0,
                SpriteMax = 0,
                TextureCount = checked((short)textures.Length),
            },
            shells, textures, [], sky.FxList, sky.ByteLength);
        cancellationToken.ThrowIfCancellationRequested();
        var export = SkyboxGltfExporter.Export(selected, "model.gltf",
            SkyboxGameProfile.ForGame(gameId).CreateExportOptions(
                "model.buffer.bin", sourceLevel, sky.Shells.Count, includeDiagnostics: false, minify: true));
        cancellationToken.ThrowIfCancellationRequested();
        return PackedFilePackageBuilder.Pack([
            new("model.gltf", export.GltfBytes, "model/gltf+json"),
            new("model.buffer.bin", export.BinBytes, "application/octet-stream"),
            .. export.Textures.Select(texture =>
                new PackedFile($"textures/{texture.FileName}", texture.PngBytes, "image/png")),
        ]);
    }
}
