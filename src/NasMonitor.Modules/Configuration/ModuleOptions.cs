using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace NasMonitor.Modules.Configuration;

public sealed class SystemModuleOptions
{
    public const string SectionName = "Modules:System";
    public bool Enabled { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 5;
    public SystemThresholdOptions Thresholds { get; init; } = new();
}

public sealed class SystemThresholdOptions
{
    public decimal CpuUsageWarningPercent { get; init; } = 90;
    public decimal CpuUsageCriticalPercent { get; init; } = 98;
    public decimal CpuTemperatureWarningCelsius { get; init; } = 80;
    public decimal CpuTemperatureCriticalCelsius { get; init; } = 95;
    public decimal MemoryUsageWarningPercent { get; init; } = 85;
    public decimal MemoryUsageCriticalPercent { get; init; } = 95;
}

public sealed class StorageOptions
{
    public const string SectionName = "Modules:Storage";
    public bool Enabled { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 5;
    public IReadOnlyList<string> MountPoints { get; init; } = ["/"];
    public UsageThresholdOptions Thresholds { get; init; } = new();
}

public sealed class UsageThresholdOptions
{
    public decimal UsageWarningPercent { get; init; } = 80;
    public decimal UsageCriticalPercent { get; init; } = 90;
}

public sealed class ZfsOptions
{
    public const string SectionName = "Modules:Zfs";
    public bool Enabled { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 10;
    public string PoolName { get; init; } = "medias";
    public bool IncludeDatasets { get; init; } = true;
    public UsageThresholdOptions Thresholds { get; init; } = new();
}

public sealed class SmartOptions
{
    public const string SectionName = "Modules:Smart";
    public bool Enabled { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 30;
    public bool DiscoverAutomatically { get; init; } = true;
    public bool WakeSleepingDisks { get; init; }
    public IReadOnlyList<string> ExcludedDevicePaths { get; init; } = [];
    public IReadOnlyList<string> ExcludedSerialNumbers { get; init; } = [];
    public SmartThresholdOptions Thresholds { get; init; } = new();
}

public sealed class SmartThresholdOptions
{
    public decimal HddTemperatureWarningCelsius { get; init; } = 45;
    public decimal HddTemperatureCriticalCelsius { get; init; } = 55;
    public decimal SataSsdTemperatureWarningCelsius { get; init; } = 60;
    public decimal SataSsdTemperatureCriticalCelsius { get; init; } = 70;
    public decimal NvmeTemperatureWarningCelsius { get; init; } = 70;
    public decimal NvmeTemperatureCriticalCelsius { get; init; } = 80;
}

public abstract class MediaOptions
{
    public bool Enabled { get; init; } = true;
    public int RequestTimeoutSeconds { get; init; } = 5;
    public string BaseUrl { get; init; } = string.Empty;
    public string ServiceUnit { get; init; } = string.Empty;
    public bool ShowSessionDetails { get; init; } = true;
    public string ApiToken { get; init; } = string.Empty;
}

public sealed class PlexOptions : MediaOptions
{
    public const string SectionName = "Modules:Plex";
}

public sealed class JellyfinOptions : MediaOptions
{
    public const string SectionName = "Modules:Jellyfin";
}

internal static partial class OptionValidation
{
    [GeneratedRegex("^[A-Za-z0-9_.:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PoolNameRegex();

    [GeneratedRegex("^[A-Za-z0-9_.@:-]+\\.service$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNameRegex();

    public static void Timeout(ICollection<string> failures, int seconds, string name)
    {
        if (seconds is < 1 or > 120)
        {
            failures.Add($"{name} must be between 1 and 120 seconds.");
        }
    }

    public static void Threshold(
        ICollection<string> failures,
        decimal warning,
        decimal critical,
        string name,
        bool percentage = false)
    {
        if (percentage && (warning is < 0 or > 100 || critical is < 0 or > 100))
        {
            failures.Add($"{name} percentages must be between 0 and 100.");
        }

        if (critical <= warning)
        {
            failures.Add($"{name} critical threshold must be greater than its warning threshold.");
        }
    }

    public static ValidateOptionsResult Result(ICollection<string> failures) =>
        failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

    public static bool IsPoolName(string value) => PoolNameRegex().IsMatch(value);
    public static bool IsServiceName(string value) => ServiceNameRegex().IsMatch(value);
}

public sealed class SystemOptionsValidator : IValidateOptions<SystemModuleOptions>
{
    public ValidateOptionsResult Validate(string? name, SystemModuleOptions options)
    {
        var failures = new List<string>();
        OptionValidation.Timeout(failures, options.TimeoutSeconds, "System timeout");
        OptionValidation.Threshold(failures, options.Thresholds.CpuUsageWarningPercent, options.Thresholds.CpuUsageCriticalPercent, "CPU usage", true);
        OptionValidation.Threshold(failures, options.Thresholds.MemoryUsageWarningPercent, options.Thresholds.MemoryUsageCriticalPercent, "Memory usage", true);
        OptionValidation.Threshold(failures, options.Thresholds.CpuTemperatureWarningCelsius, options.Thresholds.CpuTemperatureCriticalCelsius, "CPU temperature");
        return OptionValidation.Result(failures);
    }
}

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var failures = new List<string>();
        OptionValidation.Timeout(failures, options.TimeoutSeconds, "Storage timeout");
        OptionValidation.Threshold(failures, options.Thresholds.UsageWarningPercent, options.Thresholds.UsageCriticalPercent, "Storage usage", true);
        if (options.Enabled && (options.MountPoints.Count == 0 || options.MountPoints.Any(path => !path.StartsWith('/'))))
        {
            failures.Add("Storage mount points must be absolute paths.");
        }

        return OptionValidation.Result(failures);
    }
}

public sealed class ZfsOptionsValidator : IValidateOptions<ZfsOptions>
{
    public ValidateOptionsResult Validate(string? name, ZfsOptions options)
    {
        var failures = new List<string>();
        OptionValidation.Timeout(failures, options.TimeoutSeconds, "ZFS timeout");
        OptionValidation.Threshold(failures, options.Thresholds.UsageWarningPercent, options.Thresholds.UsageCriticalPercent, "ZFS usage", true);
        if (options.Enabled && (string.IsNullOrWhiteSpace(options.PoolName) || !OptionValidation.IsPoolName(options.PoolName)))
        {
            failures.Add("The enabled ZFS module requires a valid pool name.");
        }

        return OptionValidation.Result(failures);
    }
}

public sealed class SmartOptionsValidator : IValidateOptions<SmartOptions>
{
    public ValidateOptionsResult Validate(string? name, SmartOptions options)
    {
        var failures = new List<string>();
        OptionValidation.Timeout(failures, options.TimeoutSeconds, "SMART timeout");
        OptionValidation.Threshold(failures, options.Thresholds.HddTemperatureWarningCelsius, options.Thresholds.HddTemperatureCriticalCelsius, "HDD temperature");
        OptionValidation.Threshold(failures, options.Thresholds.SataSsdTemperatureWarningCelsius, options.Thresholds.SataSsdTemperatureCriticalCelsius, "SATA SSD temperature");
        OptionValidation.Threshold(failures, options.Thresholds.NvmeTemperatureWarningCelsius, options.Thresholds.NvmeTemperatureCriticalCelsius, "NVMe temperature");
        if (options.Enabled && !options.DiscoverAutomatically)
        {
            failures.Add("Automatic SMART discovery must remain enabled in V1.");
        }

        if (options.Enabled && options.ExcludedDevicePaths.Any(path => !path.StartsWith("/dev/", StringComparison.Ordinal)))
        {
            failures.Add("SMART excluded device paths must be absolute /dev paths.");
        }

        return OptionValidation.Result(failures);
    }
}

public abstract class MediaOptionsValidator<TOptions> : IValidateOptions<TOptions> where TOptions : MediaOptions
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        var failures = new List<string>();
        OptionValidation.Timeout(failures, options.RequestTimeoutSeconds, "Media request timeout");
        if (!options.Enabled)
        {
            return OptionValidation.Result(failures);
        }

        if (string.IsNullOrWhiteSpace(options.ApiToken))
        {
            failures.Add("An API token is required when the media module is enabled.");
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)
        {
            failures.Add("The media base URL must be an absolute loopback HTTP URL.");
        }

        if (!OptionValidation.IsServiceName(options.ServiceUnit))
        {
            failures.Add("The media service unit name is invalid.");
        }

        return OptionValidation.Result(failures);
    }
}

public sealed class PlexOptionsValidator : MediaOptionsValidator<PlexOptions>;
public sealed class JellyfinOptionsValidator : MediaOptionsValidator<JellyfinOptions>;
