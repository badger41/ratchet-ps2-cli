using RatchetPs2.Core.Hud;

namespace RatchetPs2.Games.DL.Hud;

public static class DlHudSpriteId
{
    public const ushort MinimumCustomValue = 0x7500;
    public const ushort MaximumCustomValue = 0x75ff;
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
