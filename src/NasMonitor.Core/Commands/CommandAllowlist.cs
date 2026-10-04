namespace NasMonitor.Core.Commands;

internal static class CommandAllowlist
{
    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.Ordinal)
    {
        "/usr/bin/vmstat",
        "/usr/bin/free",
        "/usr/bin/uptime",
        "/usr/bin/sensors",
        "/usr/bin/lsblk",
        "/usr/bin/findmnt",
        "/usr/bin/zpool",
        "/usr/bin/zfs",
        "/usr/bin/systemctl",
        "/usr/bin/sudo"
    };

    public static bool IsAllowed(string executable) => AllowedExecutables.Contains(executable);
}
