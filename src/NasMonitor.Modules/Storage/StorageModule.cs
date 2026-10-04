using Microsoft.Extensions.Options;
using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Storage;
using NasMonitor.Core.Commands;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Commands;
using NasMonitor.Modules.Configuration;

namespace NasMonitor.Modules.Storage;

public sealed class StorageModule : IStorageMonitoringModule
{
    private readonly ICommandRunner _commandRunner;
    private readonly StorageParser _parser;
    private readonly StorageOptions _options;

    public StorageModule(ICommandRunner commandRunner, StorageParser parser, IOptions<StorageOptions> options)
    {
        _commandRunner = commandRunner;
        _parser = parser;
        _options = options.Value;
    }

    public string Key => "storage";

    public async Task<StorageSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        var lsblkTask = CommandExecution.RunAsync(
            _commandRunner,
            new CommandRequest("/usr/bin/lsblk", ["--json", "--bytes", "--paths", "--output", "NAME,PATH,TYPE,TRAN,MODEL,SERIAL,SIZE,ROTA,MOUNTPOINTS"], timeout),
            cancellationToken);
        var findmntTask = CommandExecution.RunAsync(
            _commandRunner,
            new CommandRequest("/usr/bin/findmnt", ["--json", "--bytes", "--df", "--output", "SOURCE,FSTYPE,SIZE,USED,AVAIL,USE%,TARGET"], timeout),
            cancellationToken);

        await Task.WhenAll(lsblkTask, findmntTask).ConfigureAwait(false);
        return new StorageSnapshot(
            _parser.ParseMounts(findmntTask.Result.StandardOutput, _options.MountPoints.ToArray()),
            _parser.ParsePhysicalDevices(lsblkTask.Result.StandardOutput));
    }

    public ModuleState EvaluateState(StorageSnapshot snapshot)
    {
        if (snapshot.Mounts.Any(mount => mount.UsagePercent >= _options.Thresholds.UsageCriticalPercent))
        {
            return ModuleState.Critical;
        }

        return snapshot.Mounts.Any(mount => mount.UsagePercent >= _options.Thresholds.UsageWarningPercent)
            ? ModuleState.Warning
            : ModuleState.Healthy;
    }
}
