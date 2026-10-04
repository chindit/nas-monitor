using Microsoft.Extensions.Options;
using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.System;
using NasMonitor.Core.Commands;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Commands;
using NasMonitor.Modules.Configuration;

namespace NasMonitor.Modules.System;

public sealed class SystemModule : ISystemMonitoringModule
{
    private readonly ICommandRunner _commandRunner;
    private readonly SystemParser _parser;
    private readonly SystemModuleOptions _options;
    private readonly TimeProvider _timeProvider;

    public SystemModule(
        ICommandRunner commandRunner,
        SystemParser parser,
        IOptions<SystemModuleOptions> options,
        TimeProvider timeProvider)
    {
        _commandRunner = commandRunner;
        _parser = parser;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public string Key => "system";

    public async Task<SystemSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        var vmstatTask = RunVmstatAsync(timeout, cancellationToken);
        var freeTask = CommandExecution.RunAsync(_commandRunner, Request("/usr/bin/free", ["--bytes", "--wide"], timeout), cancellationToken);
        var bootTask = CommandExecution.RunAsync(_commandRunner, Request("/usr/bin/uptime", ["--since"], timeout), cancellationToken);
        var loadTask = CommandExecution.RunAsync(_commandRunner, Request("/usr/bin/uptime", [], timeout), cancellationToken);

        await Task.WhenAll(vmstatTask, freeTask, bootTask, loadTask).ConfigureAwait(false);
        var memory = _parser.ParseMemory(freeTask.Result.StandardOutput);
        var load = _parser.ParseLoadAverages(loadTask.Result.StandardOutput);
        var warnings = new List<DataWarning>();
        IReadOnlyList<TemperatureReading> temperatures = [];

        try
        {
            var sensors = await CommandExecution.RunAsync(
                _commandRunner,
                Request("/usr/bin/sensors", ["-j"], timeout),
                cancellationToken).ConfigureAwait(false);
            temperatures = _parser.ParseTemperatures(sensors.StandardOutput);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add(new DataWarning("temperature_unavailable", "Temperature readings are unavailable."));
        }

        return new SystemSnapshot(
            Environment.MachineName,
            _parser.ParseUptimeSeconds(bootTask.Result.StandardOutput, _timeProvider.GetUtcNow()),
            _parser.ParseCpuUsage(vmstatTask.Result.StandardOutput),
            load.One,
            load.Five,
            load.Fifteen,
            memory.Total,
            memory.Available,
            memory.Used,
            memory.Percent,
            temperatures,
            warnings);
    }

    public ModuleState EvaluateState(SystemSnapshot snapshot)
    {
        var thresholds = _options.Thresholds;
        var cpuTemperatures = snapshot.Temperatures.Where(IsCpuTemperature).Select(reading => reading.Celsius).ToArray();
        if (snapshot.CpuUsagePercent >= thresholds.CpuUsageCriticalPercent ||
            snapshot.MemoryUsagePercent >= thresholds.MemoryUsageCriticalPercent ||
            cpuTemperatures.Any(value => value >= thresholds.CpuTemperatureCriticalCelsius))
        {
            return ModuleState.Critical;
        }

        if (snapshot.Warnings.Count > 0 ||
            snapshot.CpuUsagePercent >= thresholds.CpuUsageWarningPercent ||
            snapshot.MemoryUsagePercent >= thresholds.MemoryUsageWarningPercent ||
            cpuTemperatures.Any(value => value >= thresholds.CpuTemperatureWarningCelsius))
        {
            return ModuleState.Warning;
        }

        return ModuleState.Healthy;
    }

    private async Task<CommandResult> RunVmstatAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var preferred = await CommandExecution.RunAsync(
            _commandRunner,
            Request("/usr/bin/vmstat", ["--wide", "--no-first", "1", "1"], timeout),
            cancellationToken,
            allowNonZeroExitCode: true).ConfigureAwait(false);
        return preferred.ExitCode == 0
            ? preferred
            : await CommandExecution.RunAsync(
                _commandRunner,
                Request("/usr/bin/vmstat", ["--wide", "1", "2"], timeout),
                cancellationToken).ConfigureAwait(false);
    }

    private static CommandRequest Request(string executable, IReadOnlyList<string> arguments, TimeSpan timeout) =>
        new(executable, arguments, timeout);

    private static bool IsCpuTemperature(TemperatureReading reading)
    {
        var text = string.Concat(reading.Source, " ", reading.Label);
        return text.Contains("core", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("package", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("tctl", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("tdie", StringComparison.OrdinalIgnoreCase);
    }
}
