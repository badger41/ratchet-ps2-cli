using System.Buffers.Binary;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Skyboxes;
using RatchetPs2.Core.Textures;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Sdk;

internal static class DlAssetResourceTests
{
    public static void Run()
    {
        var wad = new byte[7 * 2048];
        Write(wad, 0, DlLevelConstants.LevelWadHeaderSize);
        Write(wad, 0x18, 2); Write(wad, 0x1c, 2);
        Write(wad, 0x20, 4); Write(wad, 0x24, 1);
        Write(wad, 0x460, 5); Write(wad, 0x464, 1);
        Write(wad, 0x860, 6); Write(wad, 0x864, 1);
        var core = wad.AsSpan(4096);
        Write(core, 0x10, 0x100); Write(core, 0x14, 0x600);
        Write(core, 0x50, 0x800); Write(core, 0x54, 0x100);
        var header = core[0x100..];
        Write(header, 0x10, 0x80); Write(header, 0x18, 1); Write(header, 0x1c, 0xc0);
        Write(header, 0xc0, 0x10); Write(header, 0xc4, 100); header.Slice(0xd0, 16).Fill(255);
        Write(header, 0x70, 0x160); header.Slice(0x160, 16).Fill(0xee);
        Write(header, 0xb4, 0x180);
        var remap = header[0x180..];
        Write(remap, 0, 0x434); Write(remap, 4, 0x220); Write(remap, 8, 0x224); Write(remap, 12, 0x428);
        Write(remap, 0x10, 1); Write(remap, 0x20, 1);
        Write(remap, 0x220, 0); Write(remap, 0x224, 0x424); Write(remap, 0x424, 8);
        Write(remap, 0x428, 1); Write(remap, 0x42c, 2); Write(remap, 0x430, 0);
        core[0x810 + 0xd] = 2;
        var sky = new byte[0x80];
        BinaryPrimitives.WriteInt16LittleEndian(sky.AsSpan(6), 1);
        Write(sky, 0x10, 0x40); Write(sky, 0x14, 0x40); Write(sky, 0x20, 0x40);
        sky.CopyTo(core[0x880..]);
        var bank = SoundBankReaderTests.CreateDirectBank();
        bank.CopyTo(wad.AsSpan(4 * 2048)); bank.CopyTo(wad.AsSpan(6 * 2048));
        var mission = wad.AsSpan(5 * 2048);
        Write(mission, 0, 0x40); Write(mission, 8, 0x100); Write(mission, 12, 0x80);
        var classes = mission[0x100..];
        Write(classes, 0, 1); Write(classes, 0x10, 999); Write(classes, 0x14, 0x20); classes[0x20 + 0xd] = 1;
        var extracted = LevelAssetExtractor.ExtractLevelWad(GameId.DL, wad);
        Check(extracted.SkyboxBytes!.SequenceEqual(sky), "native sky extraction");
        Check(extracted.SoundBanks.Count == 2, "core and mission banks");
        Check(extracted.SoundBanks[0].Mobys.Count == 0, "SP menu bank is not used for core mobys");
        Check(extracted.SoundBanks[1].Mobys.Single(value => value.ModelArchivePath == "assets/asset_wad.bin")
            .Programs.SequenceEqual([1, 2]), "core mobys use the first mission bank");
        Check(extracted.SoundBanks[1].Mobys.Single(value => value.ModelArchivePath == "missions/0000/classes.bin")
            .Programs.SequenceEqual([0]), "mission sound mapping and provenance");
        Write(wad, 0x864, 0);
        var multiplayer = LevelAssetExtractor.ExtractLevelWad(GameId.DL, wad);
        Check(multiplayer.SoundBanks.Single().Mobys.Single().Programs.SequenceEqual([1, 2]),
            "levels without mission banks use the core bank and the separate 0xB4 moby table");
        Write(wad, 0x864, 1);
        Write(remap, 0x424, int.MaxValue);
        try { LevelAssetExtractor.ExtractLevelWad(GameId.DL, wad); throw new Exception("Expected remap bounds rejection"); }
        catch (InvalidDataException) { }

        var skyBytes = new byte[0x560];
        BinaryPrimitives.WriteInt16LittleEndian(skyBytes.AsSpan(6), 2);
        BinaryPrimitives.WriteInt16LittleEndian(skyBytes.AsSpan(12), 1);
        Write(skyBytes, 0x10, 0x30); Write(skyBytes, 0x14, 0x40);
        Write(skyBytes, 0x20, 0x540); Write(skyBytes, 0x24, 0x550);
        Write(skyBytes, 0x34, 1024); Write(skyBytes, 0x38, 16); Write(skyBytes, 0x3c, 16);
        for (var index = 0; index < 1280; index++) skyBytes[0x40 + index] = (byte)index;
        var original = skyBytes.ToArray();
        var converted = SkyboxComposer.Convert(GameId.DL, GameId.UYA, skyBytes, 7);
        using var sourceStream = new MemoryStream(skyBytes); using var targetStream = new MemoryStream(converted);
        var sourceSky = SkyboxReader.Read(sourceStream, GameId.DL); var targetSky = SkyboxReader.Read(targetStream, GameId.UYA);
        var sourceTexture = sourceSky.Textures.Single(); var targetTexture = targetSky.Textures.Single();
        Check(TextureConverter.Decode(sourceTexture.PixelData, 16, 16, TexturePixelFormat.Indexed8, sourceTexture.PaletteData,
            new() { IsSwizzled = true }).PixelData.SequenceEqual(TextureConverter.Decode(targetTexture.PixelData, 16, 16,
                TexturePixelFormat.Indexed8, targetTexture.PaletteData).PixelData), "sky texture colors survive conversion");
        Check(targetSky.Shells[1].RotationX == 0x2aaa && targetSky.Shells[1].RotationY == 0x1d4c, "DL loader rotation patch retained");
        VerifySkyPreviews(skyBytes, converted);
        Check(skyBytes.SequenceEqual(original), "source sky remains unchanged");
        Console.WriteLine("DL core/mission sounds and sky conversion checks passed.");
    }

    private static void VerifySkyPreviews(byte[] source, byte[] converted)
    {
        foreach (var (game, bytes) in new[] { (GameId.DL, source), (GameId.UYA, converted) })
        {
            var all = SkyboxPreviewPackageBuilder.Build(game, bytes, sourceLevel: 7);
            Check(all.Entries.Count == 2 && all.Entries[0].Path == "model.gltf"
                && all.Entries[1].Path == "model.buffer.bin", "preview package excludes unused textures");
            Check(Extras(all).GetProperty("ShellCount").GetInt32() == 2, "whole preview retains all shells");
            var shell = SkyboxPreviewPackageBuilder.Build(game, bytes, sourceLevel: 7, shellIndex: 1);
            var extras = Extras(shell);
            Check(extras.GetProperty("ShellCount").GetInt32() == 1
                && extras.GetProperty("TextureCount").GetInt32() == 0, "single-shell preview counts match the subset");
            Check(extras.GetProperty("RuntimeRotationPatchCount").GetInt32() == (game == GameId.DL ? 1 : 0),
                "single-shell preview uses the original shell count for loader rotation patches");
            try
            {
                SkyboxPreviewPackageBuilder.Build(game, bytes, shellIndex: 2);
                throw new Exception("Expected missing-shell rejection");
            }
            catch (InvalidDataException) { }
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                SkyboxPreviewPackageBuilder.Build(game, [], cancellationToken: cancellation.Token);
                throw new Exception("Expected sky-preview cancellation");
            }
            catch (OperationCanceledException) { }
        }
    }

    private static JsonElement Extras(RatchetPs2.Core.Wad.Models.PackedFilePackage package)
    {
        var entry = package.Entries.Single(value => value.Path == "model.gltf");
        using var document = JsonDocument.Parse(package.PackedBytes.AsMemory(entry.Offset, entry.Length));
        return document.RootElement.GetProperty("nodes")[0].GetProperty("extras").Clone();
    }

    private static void Write(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], value);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
