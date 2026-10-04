using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using NasMonitor.Contracts.Zfs;
using NasMonitor.Core.Monitoring;

namespace NasMonitor.Modules.Zfs;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Parser services are injectable singletons.")]
public sealed class ZfsParser
{
    public ZfsSnapshot Parse(string poolList, string poolStatus, string? datasetList, string expectedPoolName)
    {
        var columns = poolList.Trim().Split('\t');
        if (columns.Length != 7 || !string.Equals(columns[0], expectedPoolName, StringComparison.Ordinal))
        {
            throw ParseFailure();
        }

        return new ZfsSnapshot(
            columns[0],
            columns[6],
            ParseInt64(columns[1]),
            ParseInt64(columns[2]),
            ParseInt64(columns[3]),
            ParsePercentage(columns[4]),
            columns[5] == "-" ? null : ParsePercentage(columns[5]),
            FindPrefixedLine(poolStatus, "scan:"),
            FindPrefixedLine(poolStatus, "errors:"),
            ParseVdevs(poolStatus),
            datasetList is null ? [] : ParseDatasets(datasetList));
    }

    public IReadOnlyList<ZfsDatasetSnapshot> ParseDatasets(string output)
    {
        var result = new List<ZfsDatasetSnapshot>();
        foreach (var line in Lines(output))
        {
            var columns = line.Split('\t');
            if (columns.Length != 5)
            {
                throw ParseFailure();
            }

            result.Add(new ZfsDatasetSnapshot(
                columns[0],
                ParseInt64(columns[1]),
                ParseInt64(columns[2]),
                ParseInt64(columns[3]),
                columns[4] is "-" or "none" or "legacy" ? null : columns[4]));
        }

        return result;
    }

    public IReadOnlyList<ZfsVdevSnapshot> ParseVdevs(string output)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.None);
        var headerIndex = Array.FindIndex(lines, line =>
            line.Contains("NAME", StringComparison.Ordinal) &&
            line.Contains("STATE", StringComparison.Ordinal) &&
            line.Contains("CKSUM", StringComparison.Ordinal));
        if (headerIndex < 0)
        {
            throw ParseFailure();
        }

        var result = new List<ZfsVdevSnapshot>();
        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0)
            {
                if (result.Count > 0)
                {
                    break;
                }

                continue;
            }

            var values = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < 5 ||
                !ulong.TryParse(values[^3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var read) ||
                !ulong.TryParse(values[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var write) ||
                !ulong.TryParse(values[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var checksum))
            {
                continue;
            }

            result.Add(new ZfsVdevSnapshot(values[0], values[1], read, write, checksum));
        }

        return result.Count > 0 ? result : throw ParseFailure();
    }

    private static string? FindPrefixedLine(string output, string prefix)
    {
        var line = Lines(output).FirstOrDefault(value => value.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
        return line?.Trim()[(prefix.Length)..].Trim();
    }

    private static string[] Lines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static long ParseInt64(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : throw ParseFailure();

    private static decimal ParsePercentage(string value) =>
        decimal.TryParse(value.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : throw ParseFailure();

    private static ModuleCollectionException ParseFailure() =>
        new("parse_failed", "ZFS monitoring output could not be parsed.");
}
