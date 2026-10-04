using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using NasMonitor.Contracts.System;
using NasMonitor.Core.Monitoring;

namespace NasMonitor.Modules.System;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Parser services are injectable singletons.")]
public sealed partial class SystemParser
{
    [GeneratedRegex(@"load average:\s*([0-9]+(?:\.[0-9]+)?),\s*([0-9]+(?:\.[0-9]+)?),\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex LoadAverageRegex();

    public decimal ParseCpuUsage(string output)
    {
        var lines = NonEmptyLines(output);
        var headerIndex = Array.FindIndex(lines, line => Tokenize(line).Contains("id", StringComparer.Ordinal));
        if (headerIndex < 0 || headerIndex + 1 >= lines.Length)
        {
            throw ParseFailure();
        }

        var headers = Tokenize(lines[headerIndex]);
        var values = Tokenize(lines[^1]);
        var idleIndex = Array.IndexOf(headers, "id");
        if (idleIndex < 0 || values.Length != headers.Length || !decimal.TryParse(values[idleIndex], NumberStyles.Number, CultureInfo.InvariantCulture, out var idle))
        {
            throw ParseFailure();
        }

        return decimal.Clamp(100 - idle, 0, 100);
    }

    public (long Total, long Available, long Used, decimal Percent) ParseMemory(string output)
    {
        var memoryLine = NonEmptyLines(output).FirstOrDefault(line => line.TrimStart().StartsWith("Mem:", StringComparison.Ordinal));
        var values = memoryLine is null ? [] : Tokenize(memoryLine);
        if (values.Length < 7 || !long.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ||
            !long.TryParse(values[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var available) || total <= 0)
        {
            throw ParseFailure();
        }

        var used = Math.Max(0, total - available);
        return (total, available, used, decimal.Round(used * 100m / total, 2));
    }

    public (decimal One, decimal Five, decimal Fifteen) ParseLoadAverages(string output)
    {
        var match = LoadAverageRegex().Match(output);
        if (!match.Success)
        {
            throw ParseFailure();
        }

        return (
            decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            decimal.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            decimal.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    public long ParseUptimeSeconds(string output, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(
                output.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var bootTime))
        {
            throw ParseFailure();
        }

        return Math.Max(0, (long)(now - bootTime.ToUniversalTime()).TotalSeconds);
    }

    public IReadOnlyList<TemperatureReading> ParseTemperatures(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var readings = new List<TemperatureReading>();
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw ParseFailure();
            }

            foreach (var chip in document.RootElement.EnumerateObject())
            {
                if (chip.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var sensor in chip.Value.EnumerateObject())
                {
                    if (sensor.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var value in sensor.Value.EnumerateObject())
                    {
                        if (!value.Name.EndsWith("_input", StringComparison.Ordinal) || !TryDecimal(value.Value, out var celsius))
                        {
                            continue;
                        }

                        var prefix = value.Name[..^"_input".Length];
                        readings.Add(new TemperatureReading(
                            chip.Name,
                            string.IsNullOrWhiteSpace(sensor.Name) ? prefix : sensor.Name,
                            celsius,
                            TryPropertyDecimal(sensor.Value, prefix + "_max"),
                            TryPropertyDecimal(sensor.Value, prefix + "_crit")));
                    }
                }
            }

            return readings;
        }
        catch (JsonException exception)
        {
            throw new ModuleCollectionException("parse_failed", "Temperature data could not be parsed.", exception);
        }
    }

    private static string[] NonEmptyLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] Tokenize(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static decimal? TryPropertyDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && TryDecimal(property, out var value) ? value : null;

    private static bool TryDecimal(JsonElement element, out decimal value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value);
    }

    private static ModuleCollectionException ParseFailure() =>
        new("parse_failed", "System monitoring output could not be parsed.");
}
