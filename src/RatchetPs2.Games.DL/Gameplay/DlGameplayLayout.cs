using RatchetPs2.Core.Gameplay;

namespace RatchetPs2.Games.DL.Gameplay;

public static class DlGameplayLayout
{
    public const int CoreHeaderSize = 0x80;
    public const int MissionHeaderSize = 0x20;

    public static GameplayLayout Core { get; } = new(
        "DL",
        "core",
        CoreHeaderSize,
        [
            new(0x00, "level_settings"),
            new(0x04, "cameras"),
            new(0x08, "ambient_sound_instances"),
            new(0x0c, "us_english_strings"),
            new(0x10, "uk_english_strings"),
            new(0x14, "french_strings"),
            new(0x18, "german_strings"),
            new(0x1c, "spanish_strings"),
            new(0x20, "italian_strings"),
            new(0x24, "japanese_strings"),
            new(0x28, "korean_strings"),
            new(0x2c, "moby_classes"),
            new(0x30, "moby_instances"),
            new(0x34, "moby_groups"),
            new(0x38, "shared_data"),
            new(0x3c, "pvar_moby_links"),
            new(0x40, "pvar_table"),
            new(0x44, "pvar_data"),
            new(0x48, "pvar_relative_pointers"),
            new(0x4c, "cuboids"),
            new(0x50, "spheres"),
            new(0x54, "cylinders"),
            new(0x58, "pills"),
            new(0x5c, "splines"),
            new(0x60, "grind_splines"),
            new(0x64, "point_lights"),
            new(0x68, "pad_68"),
            new(0x6c, "camera_collision_grid"),
            new(0x70, "env_sample_points"),
            new(0x74, "areas"),
            new(0x78, "pad_78"),
            new(0x7c, "pad_7c")
        ]);

    public static GameplayLayout Mission { get; } = new(
        "DL",
        "mission",
        MissionHeaderSize,
        [
            new(0x00, "moby_classes"),
            new(0x04, "moby_instances"),
            new(0x08, "moby_groups"),
            new(0x0c, "shared_data"),
            new(0x10, "pvar_moby_links"),
            new(0x14, "pvar_table"),
            new(0x18, "pvar_data"),
            new(0x1c, "pvar_relative_pointers")
        ]);
}
