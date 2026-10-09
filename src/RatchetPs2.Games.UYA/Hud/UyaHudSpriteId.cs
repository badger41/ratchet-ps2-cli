using RatchetPs2.Core.Hud;

namespace RatchetPs2.Games.UYA.Hud;

public static class UyaHudSpriteId
{
    public const ushort MinimumCustomValue = 0xe000;
    public const ushort MaximumCustomValue = 0xefff;
    public const ushort TerminatorValue = HudSpriteId.TerminatorValue;

    public static bool IsValidCustomValue(ushort value) =>
        HudSpriteId.IsValidCustomValue(value, MinimumCustomValue, MaximumCustomValue);

    public static bool IsReserved(ushort value, IEnumerable<ushort> occupiedValues) =>
        HudSpriteId.IsReserved(value, occupiedValues);

    public static bool IsAvailable(ushort value, IEnumerable<ushort> occupiedValues) =>
        HudSpriteId.IsAvailable(value, MinimumCustomValue, MaximumCustomValue, occupiedValues);

    public static string Format(ushort value) => HudSpriteId.Format(value);

    public static bool TryParse(string? text, out ushort value) => HudSpriteId.TryParse(text, out value);
}
