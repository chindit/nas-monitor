using NasMonitor.Core.Commands;
using NasMonitor.Core.Infrastructure;
using Xunit;

namespace NasMonitor.Core.Tests;

public sealed class ProcessCommandRunnerTests
{
    [Fact]
    public async Task DashboardGateBoundsOverlappingCollections()
    {
        using var gate = new NasMonitor.Core.Services.DashboardCollectionGate();
        Assert.True(await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.False(await gate.TryEnterAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
        gate.Exit();
        Assert.True(await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None));
        gate.Exit();
    }

    [Fact]
    public async Task PassesArgumentsWithoutShellExpansion()
    {
        var (runner, host, fixture) = CreateRunner();
        var result = await runner.RunAsync(new CommandRequest(host, [fixture, "echo", "$HOME;echo bad", "*.txt"], TimeSpan.FromSeconds(10)), CancellationToken.None);
        Assert.Equal("$HOME;echo bad|*.txt", result.StandardOutput);
    }

    [Fact]
    public async Task CapturesBothStreamsConcurrently()
    {
        var (runner, host, fixture) = CreateRunner();
        var result = await runner.RunAsync(new CommandRequest(host, [fixture, "streams"], TimeSpan.FromSeconds(10), 200_000, 200_000), CancellationToken.None);
        Assert.Equal(100_000, result.StandardOutput.Length);
        Assert.Equal(100_000, result.StandardError.Length);
    }

    [Fact]
    public async Task EnforcesTimeout()
    {
        var (runner, host, fixture) = CreateRunner();
        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(new CommandRequest(host, [fixture, "sleep"], TimeSpan.FromMilliseconds(150)), CancellationToken.None));
    }

    [Fact]
    public async Task EnforcesOutputLimit()
    {
        var (runner, host, fixture) = CreateRunner();
        await Assert.ThrowsAsync<ProcessOutputLimitExceededException>(() => runner.RunAsync(new CommandRequest(host, [fixture, "flood"], TimeSpan.FromSeconds(10), 1024), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsNonAllowlistedExecutable()
    {
        var runner = new ProcessCommandRunner(new HashSet<string>(StringComparer.Ordinal));
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(new CommandRequest("/tmp/not-allowed", [], TimeSpan.FromSeconds(1)), CancellationToken.None));
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        var (runner, host, fixture) = CreateRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(new CommandRequest(host, [fixture, "sleep"], TimeSpan.FromSeconds(10)), cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task KillsChildProcessTree()
    {
        var (runner, host, fixture) = CreateRunner();
        var marker = Path.Combine(Path.GetTempPath(), $"nas-monitor-{Guid.NewGuid():N}.marker");
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(new CommandRequest(host, [fixture, "tree", marker], TimeSpan.FromMilliseconds(250)), CancellationToken.None));
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            if (File.Exists(marker)) File.Delete(marker);
        }
    }

    private static (ProcessCommandRunner Runner, string Host, string Fixture) CreateRunner()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? throw new InvalidOperationException("DOTNET_HOST_PATH is unavailable.");
        return (new ProcessCommandRunner(new HashSet<string>(StringComparer.Ordinal) { host }), host, typeof(NasMonitor.ProcessFixture.Marker).Assembly.Location);
    }
}
