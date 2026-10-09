using System.Globalization;

namespace RatchetPs2.Core.Hud;

public static class HudSpriteId
{
    public const ushort TerminatorValue = 0xffff;

    public static bool IsValidCustomValue(ushort value, ushort minimum, ushort maximum) =>
        value >= minimum && value <= maximum;

    public static bool IsReserved(ushort value, IEnumerable<ushort> occupiedValues)
    {
        ArgumentNullException.ThrowIfNull(occupiedValues);
        return value == TerminatorValue || occupiedValues.Contains(value);
    }

    public static bool IsAvailable(
        ushort value,
        ushort minimum,
        ushort maximum,
        IEnumerable<ushort> occupiedValues) =>
        IsValidCustomValue(value, minimum, maximum) && !IsReserved(value, occupiedValues);

    public static string Format(ushort value) => value.ToString("X4", CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out ushort value)
    {
        value = 0;
        if (text is null) return false;
        var valueText = text.AsSpan().Trim();
        return valueText.Length == 4
            && ushort.TryParse(valueText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}
