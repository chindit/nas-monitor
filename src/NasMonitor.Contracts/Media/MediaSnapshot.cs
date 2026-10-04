namespace NasMonitor.Contracts.Media;

public enum PlaybackState
{
    Playing,
    Paused,
    Buffering,
    Unknown
}

public enum PlaybackDecision
{
    DirectPlay,
    DirectStream,
    Transcode,
    Unknown
}

public sealed record MediaServerSnapshot(
    string Product,
    string? ServerName,
    string? Version,
    ServiceStateSnapshot Service,
    IReadOnlyList<MediaSessionSnapshot> Sessions);

public sealed record MediaSessionSnapshot(
    string SessionId,
    string? UserName,
    string MediaType,
    string DisplayTitle,
    string? ParentTitle,
    string? GrandparentTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? ClientName,
    string? DeviceName,
    PlaybackState State,
    long? PositionMilliseconds,
    long? DurationMilliseconds,
    decimal? ProgressPercent,
    PlaybackDecision Decision,
    string? VideoResolution,
    string? VideoCodec,
    string? AudioCodec,
    long? BitrateBitsPerSecond,
    decimal? TranscodeProgressPercent,
    decimal? TranscodeSpeed);

public sealed record ServiceStateSnapshot(
    string Unit,
    string LoadState,
    string ActiveState,
    string SubState,
    string? Result,
    int? MainExitStatus);
