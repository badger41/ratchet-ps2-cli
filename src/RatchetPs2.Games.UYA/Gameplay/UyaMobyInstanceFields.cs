namespace RatchetPs2.Games.UYA.Gameplay;

public enum UyaMobyInstanceField
{
    Mission,
    Bolts,
    DrawDistance,
    UpdateDistance,
    IsRooted,
    RootedDistance,
    Color,
}

public enum UyaMobyInstanceFieldValueKind
{
    Integer,
    Float,
    Boolean,
    Color,
}

public sealed record UyaMobyInstanceFieldValue(
    UyaMobyInstanceFieldValueKind Kind,
    int? Integer = null,
    float? Float = null,
    bool? Boolean = null,
    UyaRgb96? Color = null)
{
    public static UyaMobyInstanceFieldValue FromInteger(int value) => new(
        UyaMobyInstanceFieldValueKind.Integer, Integer: value);

    public static UyaMobyInstanceFieldValue FromFloat(float value) => new(
        UyaMobyInstanceFieldValueKind.Float, Float: value);

    public static UyaMobyInstanceFieldValue FromBoolean(bool value) => new(
        UyaMobyInstanceFieldValueKind.Boolean, Boolean: value);

    public static UyaMobyInstanceFieldValue FromColor(UyaRgb96 value) => new(
        UyaMobyInstanceFieldValueKind.Color, Color: value);
}

public sealed record UyaMobyInstanceFieldEdit(
    UyaMobyInstanceField Field,
    UyaMobyInstanceFieldValue Value);

public static class UyaMobyInstanceFieldLimits
{
    public const int MissionMinimum = -1;
    public const int MissionMaximum = sbyte.MaxValue;
    public const int DistanceMinimum = 0;
    public const float RootedDistanceNone = -1;
    public const int ColorMinimum = 0;
    public const int ColorMaximum = byte.MaxValue;

    internal static void Validate(UyaMobyInstanceFieldEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Value);
        var value = edit.Value;
        var populatedValues = (value.Integer is not null ? 1 : 0)
            + (value.Float is not null ? 1 : 0)
            + (value.Boolean is not null ? 1 : 0)
            + (value.Color is not null ? 1 : 0);
        if (populatedValues != 1)
            throw new ArgumentException("UYA moby field values must contain exactly one typed value.", nameof(edit));
        switch (edit.Field, value.Kind)
        {
            case (UyaMobyInstanceField.Mission, UyaMobyInstanceFieldValueKind.Integer)
                when value.Integer is >= MissionMinimum and <= MissionMaximum:
            case (UyaMobyInstanceField.Bolts or UyaMobyInstanceField.DrawDistance
                or UyaMobyInstanceField.UpdateDistance, UyaMobyInstanceFieldValueKind.Integer)
                when value.Integer is >= DistanceMinimum:
            case (UyaMobyInstanceField.IsRooted, UyaMobyInstanceFieldValueKind.Boolean)
                when value.Boolean is not null:
            case (UyaMobyInstanceField.RootedDistance, UyaMobyInstanceFieldValueKind.Float)
                when value.Float is { } distance && float.IsFinite(distance)
                    && (distance == RootedDistanceNone || distance >= 0):
            case (UyaMobyInstanceField.Color, UyaMobyInstanceFieldValueKind.Color)
                when value.Color is { } color
                    && color.Red is >= ColorMinimum and <= ColorMaximum
                    && color.Green is >= ColorMinimum and <= ColorMaximum
                    && color.Blue is >= ColorMinimum and <= ColorMaximum:
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit),
                    $"UYA moby field {edit.Field} does not accept the supplied value.");
        }
    }

    internal static UyaMobyInstanceFieldValue Read(UyaMobyInstance instance, UyaMobyInstanceField field) => field switch
    {
        UyaMobyInstanceField.Mission => UyaMobyInstanceFieldValue.FromInteger(instance.Mission),
        UyaMobyInstanceField.Bolts => UyaMobyInstanceFieldValue.FromInteger(instance.Bolts),
        UyaMobyInstanceField.DrawDistance => UyaMobyInstanceFieldValue.FromInteger(instance.DrawDistance),
        UyaMobyInstanceField.UpdateDistance => UyaMobyInstanceFieldValue.FromInteger(instance.UpdateDistance),
        UyaMobyInstanceField.IsRooted => UyaMobyInstanceFieldValue.FromBoolean(instance.IsRooted != 0),
        UyaMobyInstanceField.RootedDistance => UyaMobyInstanceFieldValue.FromFloat(instance.RootedDistance),
        UyaMobyInstanceField.Color => UyaMobyInstanceFieldValue.FromColor(instance.Color),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };
}
