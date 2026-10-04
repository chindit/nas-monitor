using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Smart;

namespace NasMonitor.SmartCollector;

internal static class Program
{
    private const string ConfigurationPath = "/etc/nas-monitor/nas-monitor.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 0)
        {
            await Console.Error.WriteLineAsync("This helper accepts no arguments.").ConfigureAwait(false);
            return 64;
        }

        try
        {
            var options = CollectorOptions.Load(ConfigurationPath);
            var discovery = await SmartctlRunner.RunAsync(["--scan-open", "--json=c"], CancellationToken.None).ConfigureAwait(false);
            if ((discovery.ExitCode & 0b111) != 0)
            {
                throw new CollectorException("SMART device discovery failed.");
            }

            var devices = SmartNormalizer.ParseDiscovery(discovery.StandardOutput);
            if (devices.Count > 64)
            {
                throw new CollectorException("SMART discovery returned too many devices.");
            }

            var snapshots = new List<SmartDeviceSnapshot>();
            var successfulCollections = 0;
            foreach (var device in devices)
            {
                if (!SmartNormalizer.IsValidDevice(device.Path, device.Protocol) || options.ExcludedDevicePaths.Contains(device.Path))
                {
                    continue;
                }

                var commandArguments = new List<string> { "--all", "--json=c" };
                if (!options.WakeSleepingDisks)
                {
                    commandArguments.Add("--nocheck=standby,0");
                }

                commandArguments.Add(device.Path);
                var result = await SmartctlRunner.RunAsync(commandArguments, CancellationToken.None).ConfigureAwait(false);
                if ((result.ExitCode & 0b111) != 0 && !SmartNormalizer.IsStandby(result.StandardOutput, result.StandardError))
                {
                    snapshots.Add(SmartNormalizer.FailedDevice(device, "SMART data could not be read for this device."));
                    continue;
                }

                var snapshot = SmartNormalizer.Normalize(device, result.StandardOutput, result.ExitCode);
                if (snapshot.SerialNumber is not null && options.ExcludedSerialNumbers.Contains(snapshot.SerialNumber))
                {
                    continue;
                }

                snapshots.Add(snapshot);
                successfulCollections++;
            }

            if (devices.Count > 0 && successfulCollections == 0)
            {
                throw new CollectorException("SMART data could not be collected for any discovered device.");
            }

            var json = JsonSerializer.Serialize(new SmartSnapshot(snapshots), SerializerOptions);
            if (Encoding.UTF8.GetByteCount(json) > 4_194_304)
            {
                throw new CollectorException("Normalized SMART output exceeded its size limit.");
            }

            await Console.Out.WriteAsync(json).ConfigureAwait(false);
            return 0;
        }
        catch (CollectorException exception)
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
        catch (Exception)
        {
            await Console.Error.WriteLineAsync("The SMART collector encountered an unexpected error.").ConfigureAwait(false);
            return 1;
        }
    }
}

internal sealed class CollectorException : Exception
{
    public CollectorException(string message, Exception? innerException = null) : base(message, innerException) { }
}

internal sealed class CollectorOptions
{
    public bool WakeSleepingDisks { get; private init; }
    public HashSet<string> ExcludedDevicePaths { get; private init; } = new(StringComparer.Ordinal);
    public HashSet<string> ExcludedSerialNumbers { get; private init; } = new(StringComparer.Ordinal);

    public static CollectorOptions Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var smart = document.RootElement.GetProperty("Modules").GetProperty("Smart");
            return new CollectorOptions
            {
                WakeSleepingDisks = smart.TryGetProperty(nameof(WakeSleepingDisks), out var wake) && wake.ValueKind == JsonValueKind.True,
                ExcludedDevicePaths = ReadStrings(smart, nameof(ExcludedDevicePaths)),
                ExcludedSerialNumbers = ReadStrings(smart, nameof(ExcludedSerialNumbers))
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        {
            throw new CollectorException("The SMART collector configuration could not be read.", exception);
        }
    }

    private static HashSet<string> ReadStrings(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return new HashSet<string>(values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()).OfType<string>(), StringComparer.Ordinal);
    }
}

internal sealed record DiscoveredDevice(string Path, string Protocol);

internal static class SmartNormalizer
{
    public static IReadOnlyList<DiscoveredDevice> ParseDiscovery(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array)
            {
                throw new CollectorException("SMART discovery returned invalid JSON.");
            }

            return devices.EnumerateArray().Select(device => new DiscoveredDevice(
                String(device, "name") ?? string.Empty,
                String(device, "protocol") ?? String(device, "type") ?? string.Empty)).ToArray();
        }
        catch (JsonException exception)
        {
            throw new CollectorException("SMART discovery returned invalid JSON.", exception);
        }
    }

    public static bool IsValidDevice(string path, string protocol)
    {
        if (!path.StartsWith("/dev/", StringComparison.Ordinal) ||
            path.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) ||
            path.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
        {
            return false;
        }

        return protocol.Equals("ATA", StringComparison.OrdinalIgnoreCase) ||
               protocol.Equals("SCSI", StringComparison.OrdinalIgnoreCase) ||
               protocol.Equals("SAS", StringComparison.OrdinalIgnoreCase) ||
               protocol.Equals("NVMe", StringComparison.OrdinalIgnoreCase) ||
               protocol.Equals("sat", StringComparison.OrdinalIgnoreCase) ||
               protocol.Equals("scsi", StringComparison.OrdinalIgnoreCase) ||
               protocol.Equals("nvme", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStandby(string standardOutput, string standardError) =>
        standardOutput.Contains("STANDBY", StringComparison.OrdinalIgnoreCase) ||
        standardError.Contains("STANDBY", StringComparison.OrdinalIgnoreCase);

    public static SmartDeviceSnapshot FailedDevice(DiscoveredDevice device, string message) =>
        new(device.Path, device.Protocol, StorageDeviceKind.Unknown, null, null, null, null, null, null, false, null, null, null, null, null, null, null, null, [message]);

    public static SmartDeviceSnapshot Normalize(DiscoveredDevice discovered, string json, int exitCode)
    {
        try
        {
            if (IsStandby(json, string.Empty))
            {
                return new SmartDeviceSnapshot(discovered.Path, discovered.Protocol, StorageDeviceKind.Unknown, null, null, null, null, null, null, true, null, null, null, null, null, null, null, null, []);
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var protocol = NestedString(root, "device", "protocol") ?? discovered.Protocol;
            var rotationRate = Int32(root, "rotation_rate");
            var kind = protocol.Equals("NVMe", StringComparison.OrdinalIgnoreCase)
                ? StorageDeviceKind.Nvme
                : rotationRate > 0 ? StorageDeviceKind.Hdd : rotationRate == 0 ? StorageDeviceKind.SataSsd : StorageDeviceKind.Unknown;
            var ataAttributes = AtaAttributes(root);
            var nvme = Object(root, "nvme_smart_health_information_log");
            var messages = HealthMessages(exitCode);
            return new SmartDeviceSnapshot(
                NestedString(root, "device", "name") ?? discovered.Path,
                protocol,
                kind,
                String(root, "model_name"),
                String(root, "serial_number"),
                String(root, "firmware_version"),
                NestedInt64(root, "user_capacity", "bytes"),
                rotationRate,
                (exitCode & 0b1000) != 0 ? false : NestedBoolean(root, "smart_status", "passed"),
                false,
                Decimal(Object(root, "temperature"), "current") ?? Decimal(nvme, "temperature"),
                NestedInt64(root, "power_on_time", "hours"),
                Decimal(nvme, "percentage_used"),
                ataAttributes.GetValueOrDefault(5),
                ataAttributes.GetValueOrDefault(197),
                ataAttributes.GetValueOrDefault(198),
                UInt64(nvme, "media_errors"),
                UInt64(nvme, "unsafe_shutdowns"),
                messages);
        }
        catch (JsonException exception)
        {
            throw new CollectorException("A SMART device returned invalid JSON.", exception);
        }
    }

    private static Dictionary<int, ulong?> AtaAttributes(JsonElement root)
    {
        var result = new Dictionary<int, ulong?>();
        var ata = Object(root, "ata_smart_attributes");
        if (ata.ValueKind != JsonValueKind.Object || !ata.TryGetProperty("table", out var table) || table.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var attribute in table.EnumerateArray())
        {
            var id = Int32(attribute, "id");
            if (id is not (5 or 197 or 198))
            {
                continue;
            }

            result[id.Value] = UInt64(Object(attribute, "raw"), "value");
        }

        return result;
    }

    private static List<string> HealthMessages(int exitCode)
    {
        var messages = new List<string>();
        if ((exitCode & 0b1000) != 0) messages.Add("SMART reports that the device is failing.");
        if ((exitCode & 0b1_0000) != 0) messages.Add("SMART attributes are at or below a threshold.");
        if ((exitCode & 0b10_0000) != 0) messages.Add("The SMART error log contains records.");
        if ((exitCode & 0b100_0000) != 0) messages.Add("The SMART self-test log contains errors.");
        if ((exitCode & 0b1000_0000) != 0) messages.Add("The SMART self-test log contains recent errors.");
        return messages;
    }

    private static JsonElement Object(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    private static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? NestedString(JsonElement element, string parent, string name) => String(Object(element, parent), name);
    private static int? Int32(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static long? NestedInt64(JsonElement element, string parent, string name) => Int64(Object(element, parent), name);
    private static long? Int64(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : null;
    private static ulong? UInt64(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetUInt64(out var result) ? result : null;
    private static decimal? Decimal(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetDecimal(out var result) ? result : null;
    private static bool? NestedBoolean(JsonElement element, string parent, string name) => Object(element, parent) is var nested && nested.ValueKind == JsonValueKind.Object && nested.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}

internal static class SmartctlRunner
{
    public static async Task<SmartctlResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/smartctl",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        process.Start();
        var outputTask = ReadLimitedAsync(process.StandardOutput, 2_097_152, timeout.Token);
        var errorTask = ReadLimitedAsync(process.StandardError, 65_536, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new SmartctlResult(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch (InvalidOperationException) { }
            throw new CollectorException("A smartctl command failed or timed out.");
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int maximumBytes, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        var bytes = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) return builder.ToString();
            bytes = checked(bytes + reader.CurrentEncoding.GetByteCount(buffer, 0, read));
            if (bytes > maximumBytes) throw new CollectorException("smartctl output exceeded its size limit.");
            builder.Append(buffer, 0, read);
        }
    }
}

internal sealed record SmartctlResult(int ExitCode, string StandardOutput, string StandardError);
