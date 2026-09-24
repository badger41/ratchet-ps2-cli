using RatchetPs2.Core.Disc;
using RatchetPs2.Core.Games;

namespace RatchetPs2.Games.UYA.Disc;

internal static class UyaDiscImageInspector
{
    private const string SupportedSerial = "SCUS-97353";

    public static DiscImageProfile SupportedProfile { get; } = new(
        GameId.UYA,
        "NTSC-U",
        "1.00",
        SupportedSerial,
        4_379_377_664,
        "ba9f2b38c7346e7b6e5b8e87717d5893");

    public static DiscImageInfo Inspect(Stream source)
    {
        var metadata = PlayStation2DiscReader.Read(source);
        return new(
            metadata.Serial.Equals(SupportedSerial, StringComparison.OrdinalIgnoreCase) ? GameId.UYA : null,
            metadata.Region,
            metadata.Revision,
            metadata.Serial,
            source.Length);
    }
}
