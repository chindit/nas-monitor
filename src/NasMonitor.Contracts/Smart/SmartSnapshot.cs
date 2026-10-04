using NasMonitor.Contracts.Common;

namespace NasMonitor.Contracts.Smart;

public sealed record SmartSnapshot(
    IReadOnlyList<SmartDeviceSnapshot> Devices);

public sealed record SmartDeviceSnapshot(
    string Path,
    string Protocol,
    StorageDeviceKind Kind,
    string? Model,
    string? SerialNumber,
    string? FirmwareVersion,
    long? CapacityBytes,
    int? RotationRateRpm,
    bool? SmartPassed,
    bool IsInStandby,
    decimal? TemperatureCelsius,
    long? PowerOnHours,
    decimal? PercentageUsed,
    ulong? ReallocatedSectorCount,
    ulong? CurrentPendingSectorCount,
    ulong? OfflineUncorrectableSectorCount,
    ulong? MediaErrors,
    ulong? UnsafeShutdowns,
    IReadOnlyList<string> HealthMessages);
