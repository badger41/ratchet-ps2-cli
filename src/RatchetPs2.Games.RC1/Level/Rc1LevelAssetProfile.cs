using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Moby;
using RatchetPs2.Games.RC1.Ties;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1LevelAssetProfile
{
    public static LevelAssetProfile Default { get; } = LevelAssetProfile.Default with
    {
        IncludeExtraMipmapDefinitions = false,
        HasMobyGsStashClassList = false,
        UseTextureFlags = false,
        MobyModelFormat = MobyModelFormat.Rc1,
        TieGameProfile = Rc1TieGameProfile.Default
    };
}
