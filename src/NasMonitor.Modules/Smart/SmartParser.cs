using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using NasMonitor.Contracts.Smart;
using NasMonitor.Core.Monitoring;

namespace NasMonitor.Modules.Smart;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Parser services are injectable singletons.")]
public sealed class SmartParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SmartSnapshot Parse(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<SmartSnapshot>(json, SerializerOptions);
            return result is { Devices: not null }
                ? result
                : throw InvalidResponse();
        }
        catch (JsonException exception)
        {
            throw InvalidResponse(exception);
        }
    }

    private static ModuleCollectionException InvalidResponse(Exception? inner = null) =>
        new("parse_failed", "The SMART collector returned invalid data.", inner);
}
