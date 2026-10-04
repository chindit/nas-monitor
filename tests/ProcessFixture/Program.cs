using System.Diagnostics;

if (args.Length == 0) return 64;
switch (args[0])
{
    case "echo":
        await Console.Out.WriteAsync(string.Join('|', args.Skip(1)));
        break;
    case "streams":
        await Task.WhenAll(Console.Out.WriteAsync(new string('o', 100_000)), Console.Error.WriteAsync(new string('e', 100_000)));
        break;
    case "sleep":
        await Task.Delay(TimeSpan.FromSeconds(30));
        break;
    case "flood":
        await Console.Out.WriteAsync(new string('x', 100_000));
        break;
    case "tree":
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath ?? throw new InvalidOperationException();
            var child = Process.Start(new ProcessStartInfo
            {
                FileName = host,
                UseShellExecute = false,
                ArgumentList = { typeof(NasMonitor.ProcessFixture.Marker).Assembly.Location, "child", args[1] }
            });
            _ = child;
            await Task.Delay(TimeSpan.FromSeconds(30));
            break;
        }
    case "child":
        await Task.Delay(TimeSpan.FromSeconds(2));
        await File.WriteAllTextAsync(args[1], "alive");
        break;
    default:
        return 64;
}

return 0;
