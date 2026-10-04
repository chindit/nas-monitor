using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NasMonitor.Contracts.Storage;
using NasMonitor.Core.Monitoring;

namespace NasMonitor.Modules.Storage;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Parser services are injectable singletons.")]
public sealed class StorageParser
{
    public IReadOnlyList<BlockDeviceSnapshot> ParsePhysicalDevices(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("blockdevices", out var devices) || devices.ValueKind != JsonValueKind.Array)
            {
                throw ParseFailure();
            }

            var result = new List<BlockDeviceSnapshot>();
            AddDisks(devices, result);
            return result;
        }
        catch (JsonException exception)
        {
            throw ParseFailure(exception);
        }
    }

    public IReadOnlyList<MountedFileSystemSnapshot> ParseMounts(string json, IReadOnlyCollection<string> includedMountPoints)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("filesystems", out var filesystems) || filesystems.ValueKind != JsonValueKind.Array)
            {
                throw ParseFailure();
            }

            var requested = new HashSet<string>(includedMountPoints, StringComparer.Ordinal);
            var returnedTargets = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<MountedFileSystemSnapshot>();
            foreach (var filesystem in filesystems.EnumerateArray())
            {
                var target = GetString(filesystem, "target");
                var fileSystemType = GetString(filesystem, "fstype");
                if (target is null || !requested.Contains(target))
                {
                    continue;
                }

                returnedTargets.Add(target);
                if (string.Equals(fileSystemType, "zfs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(new MountedFileSystemSnapshot(
                    GetString(filesystem, "source") ?? string.Empty,
                    fileSystemType ?? string.Empty,
                    target,
                    GetInt64(filesystem, "size"),
                    GetInt64(filesystem, "used"),
                    GetInt64(filesystem, "avail"),
                    GetPercentage(filesystem, "use%")));
            }

            if (requested.Any(path => !returnedTargets.Contains(path)))
            {
                throw new ModuleCollectionException("parse_failed", "A configured non-ZFS mount point was not returned by findmnt.");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw ParseFailure(exception);
        }
    }

    private static void AddDisks(JsonElement devices, ICollection<BlockDeviceSnapshot> result)
    {
        foreach (var device in devices.EnumerateArray())
        {
            if (string.Equals(GetString(device, "type"), "disk", StringComparison.Ordinal))
            {
                result.Add(new BlockDeviceSnapshot(
                    GetString(device, "path") ?? GetString(device, "name") ?? string.Empty,
                    GetString(device, "tran"),
                    GetString(device, "model"),
                    GetString(device, "serial"),
                    GetInt64(device, "size"),
                    GetBoolean(device, "rota"),
                    GetAllMountPoints(device)));
            }

            if (device.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                AddDisks(children, result);
            }
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value.ToString().Trim();
    }

    private static long GetInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw ParseFailure();
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw ParseFailure();
    }

    private static decimal GetPercentage(JsonElement element, string name)
    {
        var text = GetString(element, name)?.TrimEnd('%');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ParseFailure();
    }

    private static bool? GetBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number != 0;
        }

        return null;
    }

    private static string[] GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() is { Length: > 0 } item ? [item] : [];
        }

        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).OfType<string>().ToArray()
            : [];
    }

    private static string[] GetAllMountPoints(JsonElement device)
    {
        var mountPoints = new HashSet<string>(GetStringArray(device, "mountpoints"), StringComparer.Ordinal);
        if (device.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                mountPoints.UnionWith(GetAllMountPoints(child));
            }
        }

        return mountPoints.ToArray();
    }

    private static ModuleCollectionException ParseFailure(Exception? exception = null) =>
        new("parse_failed", "Storage monitoring output could not be parsed.", exception);
}
