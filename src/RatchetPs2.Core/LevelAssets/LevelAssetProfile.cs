using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Core.LevelAssets;

public sealed record LevelAssetProfile
{
    public static LevelAssetProfile Default { get; } = new();

    public bool IncludeExtraMipmapDefinitions { get; init; } = true;
    public bool HasMobyGsStashClassList { get; init; } = true;
    public bool UseTextureFlags { get; init; } = true;
    public MobyModelFormat MobyModelFormat { get; init; } = MobyModelFormat.Standard;
    public TieGameProfile? TieGameProfile { get; init; }
}
