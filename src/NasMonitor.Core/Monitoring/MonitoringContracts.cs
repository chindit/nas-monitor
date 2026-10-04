using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Media;
using NasMonitor.Contracts.Smart;
using NasMonitor.Contracts.Storage;
using NasMonitor.Contracts.System;
using NasMonitor.Contracts.Zfs;

namespace NasMonitor.Core.Monitoring;

public interface IMonitoringModule<TSnapshot>
{
    string Key { get; }

    Task<TSnapshot> CollectAsync(CancellationToken cancellationToken);

    ModuleState EvaluateState(TSnapshot snapshot);
}

public interface ISystemMonitoringModule : IMonitoringModule<SystemSnapshot>;
public interface IStorageMonitoringModule : IMonitoringModule<StorageSnapshot>;
public interface IZfsMonitoringModule : IMonitoringModule<ZfsSnapshot>;
public interface ISmartMonitoringModule : IMonitoringModule<SmartSnapshot>;
public interface IPlexMonitoringModule : IMonitoringModule<MediaServerSnapshot>;
public interface IJellyfinMonitoringModule : IMonitoringModule<MediaServerSnapshot>;

public sealed class ModuleCollectionException : Exception
{
    public ModuleCollectionException(string code, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Code = code;
        SafeMessage = safeMessage;
    }

    public string Code { get; }

    public string SafeMessage { get; }
}
