namespace NasMonitor.Contracts.Zfs;

public sealed record ZfsSnapshot(
    string PoolName,
    string Health,
    long SizeBytes,
    long AllocatedBytes,
    long FreeBytes,
    decimal CapacityPercent,
    decimal? FragmentationPercent,
    string? ScanSummary,
    string? ErrorSummary,
    IReadOnlyList<ZfsVdevSnapshot> Vdevs,
    IReadOnlyList<ZfsDatasetSnapshot> Datasets);

public sealed record ZfsVdevSnapshot(
    string Name,
    string State,
    ulong ReadErrors,
    ulong WriteErrors,
    ulong ChecksumErrors);

public sealed record ZfsDatasetSnapshot(
    string Name,
    long UsedBytes,
    long AvailableBytes,
    long ReferencedBytes,
    string? MountPoint);
