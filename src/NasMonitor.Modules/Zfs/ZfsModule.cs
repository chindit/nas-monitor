using Microsoft.Extensions.Options;
using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Zfs;
using NasMonitor.Core.Commands;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Commands;
using NasMonitor.Modules.Configuration;

namespace NasMonitor.Modules.Zfs;

public sealed class ZfsModule : IZfsMonitoringModule
{
    private readonly ICommandRunner _commandRunner;
    private readonly ZfsParser _parser;
    private readonly ZfsOptions _options;

    public ZfsModule(ICommandRunner commandRunner, ZfsParser parser, IOptions<ZfsOptions> options)
    {
        _commandRunner = commandRunner;
        _parser = parser;
        _options = options.Value;
    }

    public string Key => "zfs";

    public async Task<ZfsSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        var listTask = CommandExecution.RunAsync(
            _commandRunner,
            new CommandRequest("/usr/bin/zpool", ["list", "-H", "-p", "-o", "name,size,allocated,free,capacity,fragmentation,health", _options.PoolName], timeout),
            cancellationToken,
            allowNonZeroExitCode: true);
        var statusTask = CommandExecution.RunAsync(
            _commandRunner,
            new CommandRequest("/usr/bin/zpool", ["status", "-P", _options.PoolName], timeout),
            cancellationToken,
            allowNonZeroExitCode: true);
        Task<CommandResult>? datasetsTask = _options.IncludeDatasets
            ? CommandExecution.RunAsync(
                _commandRunner,
                new CommandRequest("/usr/bin/zfs", ["list", "-H", "-p", "-r", "-o", "name,used,available,referenced,mountpoint", _options.PoolName], timeout),
                cancellationToken,
                allowNonZeroExitCode: true)
            : null;

        var tasks = new List<Task> { listTask, statusTask };
        if (datasetsTask is not null)
        {
            tasks.Add(datasetsTask);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        var results = new[] { listTask.Result, statusTask.Result }.Concat(datasetsTask is null ? [] : [datasetsTask.Result]);
        if (results.Any(result => result.ExitCode != 0))
        {
            var errors = string.Join(' ', results.Select(result => result.StandardError));
            if (errors.Contains("no such pool", StringComparison.OrdinalIgnoreCase) ||
                errors.Contains("cannot open", StringComparison.OrdinalIgnoreCase))
            {
                throw new ModuleCollectionException("pool_not_found", "The configured ZFS pool was not found.");
            }

            throw new ModuleCollectionException("command_failed", "A ZFS monitoring command failed.");
        }

        return _parser.Parse(
            listTask.Result.StandardOutput,
            statusTask.Result.StandardOutput,
            datasetsTask?.Result.StandardOutput,
            _options.PoolName);
    }

    public ModuleState EvaluateState(ZfsSnapshot snapshot)
    {
        if (!string.Equals(snapshot.Health, "ONLINE", StringComparison.Ordinal) ||
            snapshot.Vdevs.Any(vdev => vdev.ReadErrors > 0 || vdev.WriteErrors > 0 || vdev.ChecksumErrors > 0) ||
            snapshot.CapacityPercent >= _options.Thresholds.UsageCriticalPercent)
        {
            return ModuleState.Critical;
        }

        return snapshot.CapacityPercent >= _options.Thresholds.UsageWarningPercent
            ? ModuleState.Warning
            : ModuleState.Healthy;
    }
}
