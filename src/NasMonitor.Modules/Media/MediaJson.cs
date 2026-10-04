using System.Globalization;
using System.Text.Json;

namespace NasMonitor.Modules.Media;

internal static class MediaJson
{
    public static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static string? String(JsonElement element, string name)
    {
        if (!TryProperty(element, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    public static long? Int64(JsonElement element, string name)
    {
        var text = String(element, name);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public static decimal? Decimal(JsonElement element, string name)
    {
        var text = String(element, name);
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public static bool? Boolean(JsonElement element, string name)
    {
        if (!TryProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    public static JsonElement? Object(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    public static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    public static decimal? Progress(long? position, long? duration)
    {
        if (position is null || duration is null || duration <= 0)
        {
            return null;
        }

        return decimal.Clamp(decimal.Round(position.Value * 100m / duration.Value, 2), 0, 100);
    }
}
