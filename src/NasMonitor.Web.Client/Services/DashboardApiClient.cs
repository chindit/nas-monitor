using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NasMonitor.Contracts.Dashboard;

namespace NasMonitor.Web.Client.Services;

public sealed class DashboardApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly HttpClient _httpClient;

    public DashboardApiClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<DashboardResponse> GetAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<DashboardResponse>(
            "api/v1/dashboard",
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        return response ?? throw new InvalidOperationException("The dashboard API returned an empty response.");
    }
}
