using RatchetPs2.Core.Disc;
using RatchetPs2.Core.Games;

namespace RatchetPs2.Games.DL.Disc;

internal static class DlDiscImageInspector
{
    private const string SupportedSerial = "SCUS-97465";

    public static DiscImageProfile SupportedProfile { get; } = new(
        GameId.DL,
        "NTSC-U",
        "1.00",
        SupportedSerial,
        4_356_898_816,
        "c1e1992076becc7de5aa70ef1de8bcd0");

    public static DiscImageInfo Inspect(Stream source)
    {
        var metadata = PlayStation2DiscReader.Read(source);
        return new(
            metadata.Serial.Equals(SupportedSerial, StringComparison.OrdinalIgnoreCase) ? GameId.DL : null,
            metadata.Region,
            metadata.Revision,
            metadata.Serial,
            source.Length);
    }
}
