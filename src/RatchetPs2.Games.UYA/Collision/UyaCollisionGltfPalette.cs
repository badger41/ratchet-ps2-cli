using System.Numerics;

namespace RatchetPs2.Games.UYA.Collision;

public sealed record UyaCollisionGltfPalette(
    IReadOnlyList<Vector3> CollisionTypeSrgbColors,
    IReadOnlyList<Vector3> SoundTypeSrgbColors,
    Vector3 PlayerBarrierSrgbColor)
{
    public static UyaCollisionGltfPalette Generic => new(
        ParseColors([
            "2F80ED", "B7F000", "405457", "8B6B3F",
            "730000", "A855F7", "5C3F3F", "005720",
            "783E00", "14B838", "4D8000", "B50000",
            "914E00", "818CF8", "38BDF8", "187800",
        ]),
        ParseColors([
            "2B2B2B", "4A6150", "826A8A", "518F8F",
            "874D84", "8A995D", "A66D6D", "008080",
            "E6BEFF", "9A6324", "FFFAC8", "800000",
            "AAFFC3", "E6194B", "3CB44B", "FFE119",
        ]),
        ParseColor("FFF200"));

    private static Vector3[] ParseColors(IReadOnlyList<string> values) =>
        values.Select(ParseColor).ToArray();

    private static Vector3 ParseColor(string value) => new(
        ParseComponent(value, 0),
        ParseComponent(value, 2),
        ParseComponent(value, 4));

    private static float ParseComponent(string value, int offset) =>
        (Hex(value[offset]) * 16 + Hex(value[offset + 1])) / 255f;

    private static int Hex(char value) => value <= '9' ? value - '0' : value - 'A' + 10;
}
