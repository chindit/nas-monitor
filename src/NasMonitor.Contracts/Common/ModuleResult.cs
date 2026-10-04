namespace NasMonitor.Contracts.Common;

public sealed record ModuleResult<T>(
    ModuleState State,
    DateTimeOffset CollectedAtUtc,
    long DurationMilliseconds,
    T? Data,
    ModuleError? Error);
