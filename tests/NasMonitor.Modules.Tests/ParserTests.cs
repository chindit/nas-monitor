using NasMonitor.Contracts.Common;
using NasMonitor.Contracts.Media;
using NasMonitor.Modules.Jellyfin;
using NasMonitor.Modules.Plex;
using NasMonitor.Modules.Storage;
using NasMonitor.Modules.System;
using NasMonitor.Modules.Zfs;
using Xunit;

namespace NasMonitor.Modules.Tests;

public sealed class ParserTests
{
    [Fact]
    public void SystemParserUsesSampledVmstatRowAndAvailableMemory()
    {
        var parser = new SystemParser();
        const string vmstat = "procs -----------memory---------- ---swap-- -----io---- -system-- ------cpu-----\n r b swpd free buff cache si so bi bo in cs us sy id wa st\n 1 0 0 1 2 3 0 0 0 0 1 2 10 5 85 0 0\n 1 0 0 1 2 3 0 0 0 0 1 2 20 5 75 0 0";
        const string free = "              total used free shared buff/cache available\nMem:     1000 700 100 0 200 400";
        Assert.Equal(25m, parser.ParseCpuUsage(vmstat));
        var memory = parser.ParseMemory(free);
        Assert.Equal(600, memory.Used);
        Assert.Equal(60m, memory.Percent);
    }

    [Fact]
    public void SystemParserHandlesMultipleSensorChipsAndNoSensors()
    {
        var parser = new SystemParser();
        const string json = """{"coretemp":{"Package":{"temp1_input":42.5,"temp1_max":90}},"nvme":{"Composite":{"temp1_input":35}}}""";
        Assert.Equal(2, parser.ParseTemperatures(json).Count);
        Assert.Empty(parser.ParseTemperatures("{}"));
    }

    [Fact]
    public void StorageParserHandlesNullsArraysAndExactMountFiltering()
    {
        var parser = new StorageParser();
        const string lsblk = """{"blockdevices":[{"name":"/dev/sda","path":"/dev/sda","type":"disk","tran":null,"model":null,"serial":null,"size":1000,"rota":1,"mountpoints":[null,"/"]}]}""";
        const string findmnt = """{"filesystems":[{"source":"/dev/sda1","fstype":"ext4","size":1000,"used":250,"avail":750,"use%":"25%","target":"/"},{"source":"tmpfs","fstype":"tmpfs","size":1,"used":0,"avail":1,"use%":"0%","target":"/run"}]}""";
        var devices = parser.ParsePhysicalDevices(lsblk);
        var mounts = parser.ParseMounts(findmnt, ["/"]);
        Assert.Single(devices);
        Assert.True(devices[0].IsRotational);
        Assert.Single(mounts);
        Assert.Equal(25m, mounts[0].UsagePercent);
    }

    [Theory]
    [InlineData("ONLINE", 0, ModuleState.Healthy)]
    [InlineData("DEGRADED", 0, ModuleState.Critical)]
    [InlineData("FAULTED", 0, ModuleState.Critical)]
    public void ZfsParserHandlesHealthStates(string health, ulong errors, ModuleState expected)
    {
        var parser = new ZfsParser();
        var list = $"medias\t1000\t200\t800\t20%\t3%\t{health}";
        var status = $@"pool: medias
 state: {health}
  scan: scrub repaired 0B in 00:01:00
config:

        NAME        STATE     READ WRITE CKSUM
        medias      {health}       {errors}     0     0
          /dev/sda  ONLINE       0     0     0

errors: No known data errors";
        var snapshot = parser.Parse(list, status, "medias\t200\t800\t200\t/medias", "medias");
        var state = snapshot.Health == "ONLINE" && snapshot.Vdevs.All(item => item.ReadErrors == 0) ? ModuleState.Healthy : ModuleState.Critical;
        Assert.Equal(expected, state);
        Assert.NotNull(snapshot.ScanSummary);
        Assert.Contains("scrub", snapshot.ScanSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void PlexParsesJsonAndXmlSessions()
    {
        var parser = new PlexParser();
        const string json = """{"MediaContainer":{"Video":[{"type":"movie","title":"Movie","duration":10000,"viewOffset":5000,"User":{"title":"user"},"Player":{"state":"playing","title":"TV"},"Session":{"id":"s1"},"Media":[{"videoDecision":"directplay","videoResolution":"1080"}]}]}}""";
        const string xml = """<MediaContainer><Video type="episode" title="Episode" duration="10000" viewOffset="1000"><User title="user"/><Player state="paused"/><Session id="s2"/><TranscodeSession videoDecision="transcode" progress="10"/></Video></MediaContainer>""";
        var jsonSession = Assert.Single(parser.ParseSessions(json, true));
        var xmlSession = Assert.Single(parser.ParseSessions(xml, false));
        Assert.Equal(PlaybackDecision.DirectPlay, jsonSession.Decision);
        Assert.Equal(PlaybackState.Paused, xmlSession.State);
        Assert.Equal(PlaybackDecision.Transcode, xmlSession.Decision);
    }

    [Fact]
    public void JellyfinFiltersIdleSessionsAndConvertsTicks()
    {
        var parser = new JellyfinParser();
        const string json = """[{"Id":"idle"},{"Id":"active","UserName":"user","Client":"Web","DeviceName":"Browser","NowPlayingItem":{"Name":"Movie","Type":"Movie","RunTimeTicks":20000000},"PlayState":{"PositionTicks":10000000,"IsPaused":false,"PlayMethod":"DirectPlay"}}]""";
        var session = Assert.Single(parser.ParseSessions(json));
        Assert.Equal(1000, session.PositionMilliseconds);
        Assert.Equal(2000, session.DurationMilliseconds);
        Assert.Equal(PlaybackDecision.DirectPlay, session.Decision);
    }
}
