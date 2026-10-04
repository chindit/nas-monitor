namespace NasMonitor.Contracts.Storage;

public sealed record StorageSnapshot(
    IReadOnlyList<MountedFileSystemSnapshot> Mounts,
    IReadOnlyList<BlockDeviceSnapshot> PhysicalDevices);

public sealed record MountedFileSystemSnapshot(
    string Source,
    string FileSystemType,
    string MountPoint,
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes,
    decimal UsagePercent);

public sealed record BlockDeviceSnapshot(
    string Path,
    string? Transport,
    string? Model,
    string? SerialNumber,
    long SizeBytes,
    bool? IsRotational,
    IReadOnlyList<string> MountPoints);
