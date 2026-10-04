using NasMonitor.Contracts.Common;
using NasMonitor.SmartCollector;
using Xunit;

namespace NasMonitor.SmartCollector.Tests;

public sealed class SmartNormalizerTests
{
    [Theory]
    [InlineData("/dev/sda", "ATA", true)]
    [InlineData("/dev/nvme0", "NVMe", true)]
    [InlineData("/dev/sg0", "SCSI", true)]
    [InlineData("relative", "ATA", false)]
    [InlineData("/dev/../etc/passwd", "ATA", false)]
    [InlineData("/dev/sda bad", "ATA", false)]
    [InlineData("/dev/sda", "unknown", false)]
    public void ValidatesDiscoveredPathsAndProtocols(string path, string protocol, bool expected) =>
        Assert.Equal(expected, SmartNormalizer.IsValidDevice(path, protocol));

    [Fact]
    public void NormalizesAtaHddAttributes()
    {
        const string json = """{"device":{"name":"/dev/sda","protocol":"ATA"},"model_name":"Disk","serial_number":"redacted","user_capacity":{"bytes":1000},"rotation_rate":7200,"smart_status":{"passed":true},"power_on_time":{"hours":10},"temperature":{"current":35},"ata_smart_attributes":{"table":[{"id":5,"raw":{"value":2}},{"id":197,"raw":{"value":1}},{"id":198,"raw":{"value":0}}]}}""";
        var device = SmartNormalizer.Normalize(new DiscoveredDevice("/dev/sda", "ATA"), json, 0);
        Assert.Equal(StorageDeviceKind.Hdd, device.Kind);
        Assert.Equal((ulong)2, device.ReallocatedSectorCount);
        Assert.Equal((ulong)1, device.CurrentPendingSectorCount);
    }

    [Fact]
    public void NormalizesSataSsdAndNvme()
    {
        const string ssd = """{"device":{"name":"/dev/sdb","protocol":"ATA"},"rotation_rate":0,"smart_status":{"passed":true}}""";
        const string nvme = """{"device":{"name":"/dev/nvme0","protocol":"NVMe"},"smart_status":{"passed":true},"nvme_smart_health_information_log":{"temperature":40,"percentage_used":5,"media_errors":1,"unsafe_shutdowns":2}}""";
        Assert.Equal(StorageDeviceKind.SataSsd, SmartNormalizer.Normalize(new DiscoveredDevice("/dev/sdb", "ATA"), ssd, 0).Kind);
        var nvmeDevice = SmartNormalizer.Normalize(new DiscoveredDevice("/dev/nvme0", "NVMe"), nvme, 0);
        Assert.Equal(StorageDeviceKind.Nvme, nvmeDevice.Kind);
        Assert.Equal((ulong)1, nvmeDevice.MediaErrors);
    }

    [Fact]
    public void InterpretsSmartHealthExitBits()
    {
        const string json = """{"device":{"name":"/dev/sda","protocol":"ATA"},"smart_status":{"passed":false}}""";
        var device = SmartNormalizer.Normalize(new DiscoveredDevice("/dev/sda", "ATA"), json, 8 | 32);
        Assert.False(device.SmartPassed);
        Assert.Contains(device.HealthMessages, message => message.Contains("failing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(device.HealthMessages, message => message.Contains("error log", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DetectsSleepingDisk()
    {
        const string json = """{"smartctl":{"argv":["smartctl","--all","--json=c","--nocheck=standby,0","/dev/sda"],"messages":[{"string":"Device is in STANDBY mode, exit(0)","severity":"info"}]},"device":{"name":"/dev/sda","protocol":"ATA"}}""";
        Assert.True(SmartNormalizer.Normalize(new DiscoveredDevice("/dev/sda", "ATA"), json, 0).IsInStandby);
    }

    [Fact]
    public void StandbyOptionDoesNotHideActiveDiskMetrics()
    {
        const string json = """{"smartctl":{"argv":["smartctl","--all","--json=c","--nocheck=standby,0","/dev/sda"]},"device":{"name":"/dev/sda","protocol":"ATA"},"model_name":"Disk","rotation_rate":7200,"smart_status":{"passed":true},"temperature":{"current":35},"power_on_time":{"hours":1234}}""";
        var device = SmartNormalizer.Normalize(new DiscoveredDevice("/dev/sda", "ATA"), json, 0);
        Assert.False(device.IsInStandby);
        Assert.Equal("Disk", device.Model);
        Assert.Equal(StorageDeviceKind.Hdd, device.Kind);
        Assert.True(device.SmartPassed);
        Assert.Equal(35m, device.TemperatureCelsius);
        Assert.Equal(1234, device.PowerOnHours);
    }

    [Fact]
    public async Task HelperRejectsEveryArgument() => Assert.Equal(64, await Program.Main(["unexpected"]));
}
