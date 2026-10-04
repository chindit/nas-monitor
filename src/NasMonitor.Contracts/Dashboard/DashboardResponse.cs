using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Media;
using NasMonitor.Contracts.Smart;
using NasMonitor.Contracts.Storage;
using NasMonitor.Contracts.System;
using NasMonitor.Contracts.Zfs;

namespace NasMonitor.Contracts.Dashboard;

public sealed record DashboardResponse(
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ModuleResult<SystemSnapshot>? System,
    ModuleResult<StorageSnapshot>? Storage,
    ModuleResult<ZfsSnapshot>? Zfs,
    ModuleResult<SmartSnapshot>? Smart,
    ModuleResult<MediaServerSnapshot>? Plex,
    ModuleResult<MediaServerSnapshot>? Jellyfin);
