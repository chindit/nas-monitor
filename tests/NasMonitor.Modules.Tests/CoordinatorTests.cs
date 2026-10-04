using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Media;
using NasMonitor.Contracts.Smart;
using NasMonitor.Contracts.Storage;
using NasMonitor.Contracts.System;
using NasMonitor.Contracts.Zfs;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Configuration;
using NasMonitor.Web.Services;
using Xunit;

namespace NasMonitor.Modules.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task DisabledModulesAreNull()
    {
        var coordinator = CreateCoordinator(
            new SystemFake(() => Task.FromResult(SystemSnapshot())),
            new StorageFake(() => Task.FromResult(StorageSnapshot())),
            enabledSystem: false,
            enabledStorage: false);
        var response = await coordinator.CollectAsync(CancellationToken.None);
        Assert.Null(response.System);
        Assert.Null(response.Storage);
        Assert.Null(response.Zfs);
        Assert.Null(response.Smart);
        Assert.Null(response.Plex);
        Assert.Null(response.Jellyfin);
    }

    [Fact]
    public async Task EnabledModulesStartConcurrentlyAndFailureIsIsolated()
    {
        var systemStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var system = new SystemFake(async () =>
        {
            systemStarted.SetResult();
            await release.Task;
            return SystemSnapshot();
        });
        var storage = new StorageFake(async () =>
        {
            storageStarted.SetResult();
            await release.Task;
            throw new ModuleCollectionException("parse_failed", "Storage failed safely.");
        });
        var coordinator = CreateCoordinator(system, storage, enabledSystem: true, enabledStorage: true);

        var collection = coordinator.CollectAsync(CancellationToken.None);
        await Task.WhenAll(systemStarted.Task, storageStarted.Task).WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult();
        var response = await collection;

        Assert.Equal(ModuleState.Healthy, response.System?.State);
        Assert.Equal(ModuleState.Unavailable, response.Storage?.State);
        Assert.Equal("parse_failed", response.Storage?.Error?.Code);
    }

    private static DashboardCoordinator CreateCoordinator(
        ISystemMonitoringModule system,
        IStorageMonitoringModule storage,
        bool enabledSystem,
        bool enabledStorage)
    {
        return new DashboardCoordinator(
            system,
            storage,
            new ZfsFake(),
            new SmartFake(),
            new PlexFake(),
            new JellyfinFake(),
            Options.Create(new SystemModuleOptions { Enabled = enabledSystem }),
            Options.Create(new StorageOptions { Enabled = enabledStorage }),
            Options.Create(new ZfsOptions { Enabled = false }),
            Options.Create(new SmartOptions { Enabled = false }),
            Options.Create(new PlexOptions { Enabled = false }),
            Options.Create(new JellyfinOptions { Enabled = false }),
            TimeProvider.System,
            NullLogger<DashboardCoordinator>.Instance);
    }

    private static SystemSnapshot SystemSnapshot() => new("nas", 1, 1, 1, 1, 1, 100, 90, 10, 10, [], []);
    private static StorageSnapshot StorageSnapshot() => new([], []);

    private sealed class SystemFake(Func<Task<SystemSnapshot>> collect) : ISystemMonitoringModule
    {
        public string Key => "system";
        public Task<SystemSnapshot> CollectAsync(CancellationToken cancellationToken) => collect();
        public ModuleState EvaluateState(SystemSnapshot snapshot) => ModuleState.Healthy;
    }

    private sealed class StorageFake(Func<Task<StorageSnapshot>> collect) : IStorageMonitoringModule
    {
        public string Key => "storage";
        public Task<StorageSnapshot> CollectAsync(CancellationToken cancellationToken) => collect();
        public ModuleState EvaluateState(StorageSnapshot snapshot) => ModuleState.Healthy;
    }

    private sealed class ZfsFake : IZfsMonitoringModule
    {
        public string Key => "zfs";
        public Task<ZfsSnapshot> CollectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ModuleState EvaluateState(ZfsSnapshot snapshot) => ModuleState.Healthy;
    }

    private sealed class SmartFake : ISmartMonitoringModule
    {
        public string Key => "smart";
        public Task<SmartSnapshot> CollectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ModuleState EvaluateState(SmartSnapshot snapshot) => ModuleState.Healthy;
    }

    private sealed class PlexFake : IPlexMonitoringModule
    {
        public string Key => "plex";
        public Task<MediaServerSnapshot> CollectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ModuleState EvaluateState(MediaServerSnapshot snapshot) => ModuleState.Healthy;
    }

    private sealed class JellyfinFake : IJellyfinMonitoringModule
    {
        public string Key => "jellyfin";
        public Task<MediaServerSnapshot> CollectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ModuleState EvaluateState(MediaServerSnapshot snapshot) => ModuleState.Healthy;
    }
}
