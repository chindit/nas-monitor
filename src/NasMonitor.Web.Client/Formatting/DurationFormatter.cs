using System.Globalization;

namespace NasMonitor.Web.Client.Formatting;

public static class DurationFormatter
{
    public static string FormatLong(long seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalDays >= 1
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalDays}d {duration.Hours}h")
            : string.Create(CultureInfo.CurrentCulture, $"{duration.Hours}h {duration.Minutes}m");
    }

    public static string FormatPlayback(long? milliseconds) => milliseconds is null
        ? "—"
        : TimeSpan.FromMilliseconds(Math.Max(0, milliseconds.Value)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
