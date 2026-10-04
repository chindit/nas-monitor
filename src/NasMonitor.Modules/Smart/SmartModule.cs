using Microsoft.Extensions.Options;
using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Smart;
using NasMonitor.Core.Commands;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Commands;
using NasMonitor.Modules.Configuration;

namespace NasMonitor.Modules.Smart;

public sealed class SmartModule : ISmartMonitoringModule
{
    private const string CollectorPath = "/usr/local/libexec/nas-monitor-smart-collector";
    private readonly ICommandRunner _commandRunner;
    private readonly SmartParser _parser;
    private readonly SmartOptions _options;

    public SmartModule(ICommandRunner commandRunner, SmartParser parser, IOptions<SmartOptions> options)
    {
        _commandRunner = commandRunner;
        _parser = parser;
        _options = options.Value;
    }

    public string Key => "smart";

    public async Task<SmartSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var result = await CommandExecution.RunAsync(
            _commandRunner,
            new CommandRequest(
                "/usr/bin/sudo",
                ["-n", "--", CollectorPath],
                TimeSpan.FromSeconds(_options.TimeoutSeconds),
                MaximumStandardOutputBytes: 4_194_304),
            cancellationToken,
            allowNonZeroExitCode: true).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var code = result.StandardError.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                       result.StandardError.Contains("not allowed", StringComparison.OrdinalIgnoreCase)
                ? "permission_denied"
                : "command_failed";
            throw new ModuleCollectionException(code, "The privileged SMART collector failed.");
        }

        var snapshot = _parser.Parse(result.StandardOutput);
        if (snapshot.Devices.Count == 0)
        {
            throw new ModuleCollectionException("parse_failed", "The SMART collector returned no devices.");
        }

        return snapshot;
    }

    public ModuleState EvaluateState(SmartSnapshot snapshot)
    {
        var state = ModuleState.Healthy;
        foreach (var device in snapshot.Devices)
        {
            if (device.SmartPassed == false || IsCriticalTemperature(device))
            {
                return ModuleState.Critical;
            }

            if (IsWarningTemperature(device) ||
                device.ReallocatedSectorCount > 0 ||
                device.CurrentPendingSectorCount > 0 ||
                device.OfflineUncorrectableSectorCount > 0 ||
                device.MediaErrors > 0 ||
                device.HealthMessages.Count > 0)
            {
                state = ModuleState.Warning;
            }
        }

        return state;
    }

    private bool IsWarningTemperature(SmartDeviceSnapshot device) =>
        device.TemperatureCelsius is { } temperature && temperature >= TemperatureThresholds(device.Kind).Warning;

    private bool IsCriticalTemperature(SmartDeviceSnapshot device) =>
        device.TemperatureCelsius is { } temperature && temperature >= TemperatureThresholds(device.Kind).Critical;

    private (decimal Warning, decimal Critical) TemperatureThresholds(StorageDeviceKind kind) => kind switch
    {
        StorageDeviceKind.Hdd => (_options.Thresholds.HddTemperatureWarningCelsius, _options.Thresholds.HddTemperatureCriticalCelsius),
        StorageDeviceKind.Nvme => (_options.Thresholds.NvmeTemperatureWarningCelsius, _options.Thresholds.NvmeTemperatureCriticalCelsius),
        _ => (_options.Thresholds.SataSsdTemperatureWarningCelsius, _options.Thresholds.SataSsdTemperatureCriticalCelsius)
    };
}
