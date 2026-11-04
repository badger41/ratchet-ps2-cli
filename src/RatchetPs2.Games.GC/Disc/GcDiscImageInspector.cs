using RatchetPs2.Core.Disc;
using RatchetPs2.Core.Games;

namespace RatchetPs2.Games.GC.Disc;

internal static class GcDiscImageInspector
{
    private const string SupportedSerial = "SCUS-97268";

    public static DiscImageProfile SupportedProfile { get; } = new(
        GameId.GC,
        "NTSC-U",
        "1.01",
        SupportedSerial,
        3_828_350_976,
        "3cbbb5127ee8a0be93ef0876f7781ee8");

    public static DiscImageInfo Inspect(Stream source)
    {
        var metadata = PlayStation2DiscReader.Read(source);
        return new(
            metadata.Serial.Equals(SupportedSerial, StringComparison.OrdinalIgnoreCase) ? GameId.GC : null,
            metadata.Region,
            metadata.Revision,
            metadata.Serial,
            source.Length);
    }
}
