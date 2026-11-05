using RatchetPs2.Core.Disc;
using RatchetPs2.Core.Games;

namespace RatchetPs2.Games.RC1.Disc;

internal static class Rc1DiscImageInspector
{
    private const string SupportedSerial = "SCUS-97199";
    // Retail image identity: https://retroachievements.org/forums/topic/18041
    public static DiscImageProfile SupportedProfile { get; } = new(
        GameId.RC1, "NTSC-U", "1.00", SupportedSerial, 4_214_095_872, "a62a8ae0e44c2580be38c82f48b7d181");

    public static DiscImageInfo Inspect(Stream source)
    {
        var metadata = PlayStation2DiscReader.Read(source);
        return new(metadata.Serial.Equals(SupportedSerial, StringComparison.OrdinalIgnoreCase) ? GameId.RC1 : null,
            metadata.Region, metadata.Revision, metadata.Serial, source.Length);
    }
}
