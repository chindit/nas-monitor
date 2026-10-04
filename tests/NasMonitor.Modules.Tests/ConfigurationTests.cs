using NasMonitor.Modules.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace NasMonitor.Modules.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void DisabledMediaModuleMayOmitSecretsAndAddresses()
    {
        var result = new PlexOptionsValidator().Validate(null, new PlexOptions { Enabled = false });
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void EnabledMediaModuleRequiresTokenLoopbackUrlAndServiceName()
    {
        var validator = new JellyfinOptionsValidator();
        Assert.True(validator.Validate(null, new JellyfinOptions
        {
            Enabled = true,
            ApiToken = "secret",
            BaseUrl = "http://127.0.0.1:8096",
            ServiceUnit = "jellyfin.service"
        }).Succeeded);
        Assert.False(validator.Validate(null, new JellyfinOptions
        {
            Enabled = true,
            ApiToken = "secret",
            BaseUrl = "https://example.com",
            ServiceUnit = "bad unit"
        }).Succeeded);
    }

    [Fact]
    public void InvalidThresholdsAndPoolNamesFailValidation()
    {
        var zfs = new ZfsOptions
        {
            Enabled = true,
            PoolName = "bad pool",
            Thresholds = new UsageThresholdOptions { UsageWarningPercent = 90, UsageCriticalPercent = 80 }
        };
        Assert.False(new ZfsOptionsValidator().Validate(null, zfs).Succeeded);
    }

    [Fact]
    public void InvalidSmartExclusionPathFailsValidation()
    {
        var smart = new SmartOptions { Enabled = true, ExcludedDevicePaths = ["relative"] };
        Assert.False(new SmartOptionsValidator().Validate(null, smart).Succeeded);
    }

    [Fact]
    public void LaterConfigurationProviderOverridesEarlierJsonValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:Plex:ApiToken"] = "json-value" })
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:Plex:ApiToken"] = "environment-value" })
            .Build();
        Assert.Equal("environment-value", configuration["Modules:Plex:ApiToken"]);
    }
}
