using System.Diagnostics;
using Microsoft.Extensions.Options;
using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Dashboard;
using NasMonitor.Core.Commands;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Services;
using NasMonitor.Modules.Configuration;

namespace NasMonitor.Web.Services;

public sealed partial class DashboardCoordinator : IDashboardCoordinator
{
    private readonly ISystemMonitoringModule _system;
    private readonly IStorageMonitoringModule _storage;
    private readonly IZfsMonitoringModule _zfs;
    private readonly ISmartMonitoringModule _smart;
    private readonly IPlexMonitoringModule _plex;
    private readonly IJellyfinMonitoringModule _jellyfin;
    private readonly SystemModuleOptions _systemOptions;
    private readonly StorageOptions _storageOptions;
    private readonly ZfsOptions _zfsOptions;
    private readonly SmartOptions _smartOptions;
    private readonly PlexOptions _plexOptions;
    private readonly JellyfinOptions _jellyfinOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DashboardCoordinator> _logger;

    public DashboardCoordinator(
        ISystemMonitoringModule system,
        IStorageMonitoringModule storage,
        IZfsMonitoringModule zfs,
        ISmartMonitoringModule smart,
        IPlexMonitoringModule plex,
        IJellyfinMonitoringModule jellyfin,
        IOptions<SystemModuleOptions> systemOptions,
        IOptions<StorageOptions> storageOptions,
        IOptions<ZfsOptions> zfsOptions,
        IOptions<SmartOptions> smartOptions,
        IOptions<PlexOptions> plexOptions,
        IOptions<JellyfinOptions> jellyfinOptions,
        TimeProvider timeProvider,
        ILogger<DashboardCoordinator> logger)
    {
        _system = system;
        _storage = storage;
        _zfs = zfs;
        _smart = smart;
        _plex = plex;
        _jellyfin = jellyfin;
        _systemOptions = systemOptions.Value;
        _storageOptions = storageOptions.Value;
        _zfsOptions = zfsOptions.Value;
        _smartOptions = smartOptions.Value;
        _plexOptions = plexOptions.Value;
        _jellyfinOptions = jellyfinOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DashboardResponse> CollectAsync(CancellationToken cancellationToken)
    {
        var requestedAt = _timeProvider.GetUtcNow();
        var systemTask = _systemOptions.Enabled ? CollectModuleAsync(_system, _systemOptions.TimeoutSeconds, cancellationToken) : null;
        var storageTask = _storageOptions.Enabled ? CollectModuleAsync(_storage, _storageOptions.TimeoutSeconds, cancellationToken) : null;
        var zfsTask = _zfsOptions.Enabled ? CollectModuleAsync(_zfs, _zfsOptions.TimeoutSeconds, cancellationToken) : null;
        var smartTask = _smartOptions.Enabled ? CollectModuleAsync(_smart, _smartOptions.TimeoutSeconds, cancellationToken) : null;
        var plexTask = _plexOptions.Enabled ? CollectModuleAsync(_plex, _plexOptions.RequestTimeoutSeconds, cancellationToken) : null;
        var jellyfinTask = _jellyfinOptions.Enabled ? CollectModuleAsync(_jellyfin, _jellyfinOptions.RequestTimeoutSeconds, cancellationToken) : null;

        var tasks = new Task?[] { systemTask, storageTask, zfsTask, smartTask, plexTask, jellyfinTask }.OfType<Task>();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return new DashboardResponse(
            requestedAt,
            _timeProvider.GetUtcNow(),
            systemTask?.Result,
            storageTask?.Result,
            zfsTask?.Result,
            smartTask?.Result,
            plexTask?.Result,
            jellyfinTask?.Result);
    }

    private async Task<ModuleResult<TSnapshot>> CollectModuleAsync<TSnapshot>(
        IMonitoringModule<TSnapshot> module,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var snapshot = await module.CollectAsync(timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new ModuleResult<TSnapshot>(
                module.EvaluateState(snapshot),
                _timeProvider.GetUtcNow(),
                stopwatch.ElapsedMilliseconds,
                snapshot,
                null);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var error = MapError(exception, timeout, cancellationToken);
            LogModuleFailure(_logger, module.Key, error.Code);
            return new ModuleResult<TSnapshot>(
                ModuleState.Unavailable,
                _timeProvider.GetUtcNow(),
                stopwatch.ElapsedMilliseconds,
                default,
                error);
        }
    }

    private static ModuleError MapError(
        Exception exception,
        CancellationTokenSource timeout,
        CancellationToken callerToken)
    {
        if (exception is ModuleCollectionException moduleException)
        {
            return new ModuleError(moduleException.Code, moduleException.SafeMessage);
        }

        if (exception is ProcessOutputLimitExceededException)
        {
            return new ModuleError("output_limit_exceeded", "A monitoring command produced too much output.");
        }

        if (exception is TimeoutException || timeout.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            return new ModuleError("command_timed_out", "The module collection timed out.");
        }

        if (exception is OperationCanceledException || callerToken.IsCancellationRequested)
        {
            return new ModuleError("collection_cancelled", "The module collection was cancelled.");
        }

        return new ModuleError("command_failed", "The module could not be collected.");
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Module {moduleKey} collection failed with code {errorCode}.")]
    private static partial void LogModuleFailure(ILogger logger, string moduleKey, string errorCode);
}
