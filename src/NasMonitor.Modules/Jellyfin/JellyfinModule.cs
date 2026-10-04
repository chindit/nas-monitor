using Microsoft.Extensions.Options;
using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Media;
using NasMonitor.Core.Monitoring;
using NasMonitor.Modules.Configuration;
using NasMonitor.Modules.Media;

namespace NasMonitor.Modules.Jellyfin;

public sealed class JellyfinModule : IJellyfinMonitoringModule
{
    private readonly JellyfinApiClient _apiClient;
    private readonly IServiceStateReader _serviceStateReader;
    private readonly JellyfinOptions _options;

    public JellyfinModule(JellyfinApiClient apiClient, IServiceStateReader serviceStateReader, IOptions<JellyfinOptions> options)
    {
        _apiClient = apiClient;
        _serviceStateReader = serviceStateReader;
        _options = options.Value;
    }

    public string Key => "jellyfin";

    public async Task<MediaServerSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var apiTask = _apiClient.ReadAsync(cancellationToken);
        var serviceTask = ReadServiceStateAsync(cancellationToken);
        var service = await serviceTask.ConfigureAwait(false);
        try
        {
            var api = await apiTask.ConfigureAwait(false);
            return new MediaServerSnapshot("Jellyfin", api.Name, api.Version, service, api.Sessions);
        }
        catch (ModuleCollectionException) when (IsStopped(service))
        {
            return new MediaServerSnapshot("Jellyfin", null, null, service, []);
        }
    }

    public ModuleState EvaluateState(MediaServerSnapshot snapshot) => MediaStateEvaluator.Evaluate(snapshot.Service);

    private async Task<ServiceStateSnapshot> ReadServiceStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _serviceStateReader.ReadAsync(_options.ServiceUnit, TimeSpan.FromSeconds(_options.RequestTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ServiceStateSnapshot(_options.ServiceUnit, "unknown", "unknown", "unknown", null, null);
        }
    }

    private static bool IsStopped(ServiceStateSnapshot service) =>
        service.ActiveState is "inactive" or "failed";
}
