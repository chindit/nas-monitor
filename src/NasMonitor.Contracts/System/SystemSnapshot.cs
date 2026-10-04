namespace NasMonitor.Contracts.System;

public sealed record SystemSnapshot(
    string HostName,
    long UptimeSeconds,
    decimal CpuUsagePercent,
    decimal LoadAverage1Minute,
    decimal LoadAverage5Minutes,
    decimal LoadAverage15Minutes,
    long MemoryTotalBytes,
    long MemoryAvailableBytes,
    long MemoryUsedBytes,
    decimal MemoryUsagePercent,
    IReadOnlyList<TemperatureReading> Temperatures,
    IReadOnlyList<DataWarning> Warnings);

public sealed record TemperatureReading(
    string Source,
    string Label,
    decimal Celsius,
    decimal? ReportedHighCelsius,
    decimal? ReportedCriticalCelsius);

public sealed record DataWarning(string Code, string Message);
