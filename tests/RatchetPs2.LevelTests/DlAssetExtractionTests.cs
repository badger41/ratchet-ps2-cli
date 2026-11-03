using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Sdk;

internal static class DlAssetExtractionTests
{
    public static void Run()
    {
        CheckMissionTextureAllocation();
        CheckTextureSlots();
        var profile = DiscImageInspector.GetSupportedProfile(GameId.DL);
        Check(profile.Serial == "SCUS-97465" && profile.Md5 == "c1e1992076becc7de5aa70ef1de8bcd0"
            && profile.Size == 4_356_898_816, "retail disc profile");
        var wad = CreateWad();
        var result = LevelAssetExtractor.ExtractLevelWad(GameId.DL, wad);
        Check(result.FailedAssetCount == 0 && result.Assets.Count == 4, "core and mission extraction");
        Check(result.Assets.Select(asset => asset.Kind).Distinct().Count() == 3, "three model kinds");
        Check(result.Assets.Single(asset => asset.Kind == FrontendAssetKind.Tie).ModelBytes.Length == 0x10,
            "model slices stop at the next definition");
        var coreMoby = result.Assets.Single(asset => asset.Kind == FrontendAssetKind.Moby && asset.SourceArchivePath is null);
        Check(coreMoby.Textures.Single().PifBytes is null, "unavailable textures remain explicit");
        var missionMoby = result.Assets.Single(asset => asset.SourceArchivePath is not null);
        Check(missionMoby.ClassId == 999 && missionMoby.SourceArchivePath == "missions/0000/classes.bin"
            && missionMoby.SourceIndex == 0, "mission provenance");
        var definition = LevelAssetCanonicalizer.NormalizeDefinition(GameId.DL, coreMoby.DefinitionBytes);
        Check(definition.AsSpan(0, 8).ToArray().All(value => value == 0), "volatile definition references normalized");
        Check(coreMoby.DefinitionBytes[0] != 0, "normalization preserves input");
        Write(wad, 0x18, int.MaxValue);
        try { LevelAssetExtractor.ExtractLevelWad(GameId.DL, wad); throw new Exception("Expected bounds rejection"); }
        catch (InvalidDataException) { }
        Console.WriteLine("DL asset extraction tests passed.");
    }

    private static void CheckTextureSlots()
    {
        var pixels = Enumerable.Range(0, 256).Select(index => (byte)index).ToArray();
        LevelAssetTextureDefinition[] definitions = [new(0, 0, 16, 16, 1, 0, -1, 0)];
        foreach (var swizzled in new[] { false, true })
        {
            var textures = LevelAssetTextureExtractor.Extract("moby", [byte.MaxValue, 3, 0, 4],
                definitions, new byte[1024], pixels, 0, null, swizzled);
            Check(textures.Count == 3, "absent texture IDs do not create slots");
            Check(textures[0].PifBytes is null && textures[0].Error!.Contains("ID 3")
                && textures[2].PifBytes is null && textures[2].Error!.Contains("ID 4"),
                "unavailable texture slots stay ordered around a valid texture");
            using var stream = new MemoryStream(textures[1].PifBytes!);
            var texture = PifReader.Read(stream);
            Check(texture.IsSwizzled == swizzled && texture.PixelData.SequenceEqual(pixels),
                "shared extraction preserves the game's swizzle flag and source pixels");
        }
    }

    private static void CheckMissionTextureAllocation()
    {
        var pif = PifWriter.Write(PifWriter.CreateIndexed8(8, 8, new byte[1024],
            Enumerable.Range(0, 64).Select(index => (byte)index).ToArray(), [new byte[16], new byte[4]]));
        var bank = new byte[0x50 + pif.Length];
        Write(bank, 0, 1); Write(bank, 0x10, 0x209a); Write(bank, 0x14, 0x20); Write(bank, 0x18, 0x40);
        Write(bank, 0x40, 1); Write(bank, 0x44, 0x10); pif.CopyTo(bank, 0x50);
        Write(bank, 0x54, 6643361);
        var original = bank.ToArray();
        var texture = DlMissionMobyBankReader.Read(bank).Single().PifTextures.Single();
        Check(texture.SequenceEqual(pif), "mission bank extraction normalizes embedded PIF sizes and preserves all texture data");
        Check(bank.SequenceEqual(original), "mission texture normalization preserves the source bank");
    }

    internal static byte[] CreateWad()
    {
        var wad = new byte[4 * DlLevelConstants.SectorSize];
        Write(wad, 0, DlLevelConstants.LevelWadHeaderSize);
        Write(wad, 0x18, 2); Write(wad, 0x1c, 1);
        Write(wad, 0x460, 3); Write(wad, 0x464, 1);
        var core = wad.AsSpan(2 * DlLevelConstants.SectorSize);
        Write(core, 0x10, 0x100); Write(core, 0x14, 0x200);
        Write(core, 0x18, 0x300); Write(core, 0x1c, 1);
        Write(core, 0x50, 0x400); Write(core, 0x54, 0x50);
        var header = core[0x100..];
        Write(header, 0x18, 1); Write(header, 0x1c, 0xc0);
        Write(header, 0x20, 1); Write(header, 0x24, 0xe0);
        Write(header, 0x28, 1); Write(header, 0x2c, 0x100);
        Definition(header, 0xc0, 0x10, 100); header[0xd0] = 0;
        Definition(header, 0xe0, 0x20, 200);
        Definition(header, 0x100, 0x30, 300);
        core.Slice(0x410, 0x10).Fill(0x11);
        core.Slice(0x420, 0x10).Fill(0x22);
        core.Slice(0x430, 0x20).Fill(0x33);
        var mission = wad.AsSpan(3 * DlLevelConstants.SectorSize);
        Write(mission, 0, 0x40); Write(mission, 8, 0x100); Write(mission, 12, 0x40);
        var classes = mission[0x100..];
        Write(classes, 0, 1); Write(classes, 0x10, 999); Write(classes, 0x14, 0x20);
        classes.Slice(0x20, 0x20).Fill(0x44);
        return wad;
    }

    private static void Definition(Span<byte> data, int offset, int modelOffset, int id)
    {
        Write(data, offset, modelOffset); Write(data, offset + 4, id);
        data.Slice(offset + 0x10, 0x10).Fill(255);
    }

    private static void Write(Span<byte> data, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(data[offset..], value);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
