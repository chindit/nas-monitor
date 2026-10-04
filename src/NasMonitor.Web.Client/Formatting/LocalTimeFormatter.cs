using System.Globalization;

namespace NasMonitor.Web.Client.Formatting;

public static class LocalTimeFormatter
{
    public static string Format(DateTimeOffset value) => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
