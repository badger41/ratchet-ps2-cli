using RatchetPs2.Core.Games;

namespace RatchetPs2.Core.Disc;

public sealed record DiscImageInfo(
    GameId? Game,
    string Region,
    string Revision,
    string Serial,
    long Size);

public sealed record DiscImageProfile(
    GameId Game,
    string Region,
    string Revision,
    string Serial,
    long Size,
    string Md5);

public sealed record PlayStation2DiscMetadata(
    string Region,
    string Revision,
    string Serial);
