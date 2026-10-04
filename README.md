# NAS Monitor

A dashboard for a Linux NAS: system metrics, disk space, ZFS pool health, SMART disk health, Plex, and Jellyfin. The ZFS pool's used space also appears in the **Storage** card alongside the system disk.

The deployment in this repository targets an **Arch Linux x86_64** NAS with systemd. CI builds a self-contained Linux archive, so the NAS does not need the .NET SDK to run it. The SDK is needed for local development and builds.

## Requirements on the NAS

- A working ZFS pool if the ZFS module is enabled.
- `vmstat`, `free`, `uptime`, `sensors`, `lsblk`, `findmnt`, `zpool`, `zfs`, `systemctl`, `sudo`, and `smartctl`. The collector expects these tools under `/usr/bin`.
- Caddy if the dashboard will be accessed over the network with HTTPS and authentication.

On the NAS:

```bash
sudo pacman -S --needed git curl smartmontools lm_sensors sudo
git clone https://github.com/chindit/nas-monitor.git
cd nas-monitor
command -v vmstat free uptime sensors lsblk findmnt zpool zfs systemctl sudo smartctl
sudo bash deploy/install.sh
```

The installer creates the `nas-monitor` system user, installs the systemd service and a sudo rule limited to the SMART collector, and copies the example configuration files **only when the destination files do not already exist**.

## Configure the modules

Edit `/etc/nas-monitor/nas-monitor.json` after installation. The main settings are:

| Setting | What to change |
| --- | --- |
| `AllowedHosts` | The DNS name used for the dashboard, alongside `localhost` and `127.0.0.1`. |
| `Modules:Storage:MountPoints` | Mount points to display, such as `/` for the system disk. |
| `Modules:Zfs:PoolName` | The exact ZFS pool name; the example uses `medias`. |
| `Modules:Plex` and `Modules:Jellyfin` | Local URLs and systemd service names for your NAS. Set `Enabled` to `false` for a service you do not run. |
| `Modules:Smart` | Optional disk exclusions and whether to wake sleeping disks. |

Do not add the ZFS pool to `Storage.MountPoints`. Its capacity comes from `zpool list` and appears in the **Storage** card when the ZFS module is enabled. These figures describe the entire pool, not usage per member disk.

Put Plex and Jellyfin API tokens in `/etc/nas-monitor/nas-monitor.env`, replacing `replace-me` for enabled modules. Keep this file owned by `root` and accessible only to `root` (`0600`). Do not put real tokens in the repository or the example JSON file.

## Deploy a version

Each push to `main` starts the GitHub Actions workflow. After a successful build, download the **`nas-monitor-linux-x64`** artifact from that workflow run. It contains `nas-monitor-<version>.tar.gz` and the adjacent `nas-monitor-<version>.tar.gz.sha256` file.

Copy both files to the NAS, then run the deployment script from the repository clone. Replace `1.0.42` with the version in the archive filename:

```bash
sudo bash deploy/deploy.sh 1.0.42 /path/to/nas-monitor-1.0.42.tar.gz
```

The script checks the SHA-256 checksum, installs the version under `/opt/nas-monitor/releases`, updates `/opt/nas-monitor/current`, restarts the service, and checks `/health/live`. If the health check fails, it restores the previous version when one exists.

## HTTPS access

Adapt [`deploy/Caddyfile.example`](deploy/Caddyfile.example) to your DNS name and a password hash generated on the NAS:

```bash
caddy hash-password --algorithm argon2id
caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

Integrate the example into the Caddy configuration actually used on your NAS. NAS Monitor listens on `127.0.0.1:5180`; do not expose that port directly to the network. Access the dashboard through the HTTPS URL configured in Caddy.

## Verify and troubleshoot

```bash
systemctl status nas-monitor.service
curl http://127.0.0.1:5180/health/live
journalctl -u nas-monitor.service
sudo -u nas-monitor sudo -n -- /usr/local/libexec/nas-monitor-smart-collector
```

Changes to `/etc/nas-monitor/nas-monitor.json` or `/etc/nas-monitor/nas-monitor.env` require `sudo systemctl restart nas-monitor.service`. If temperatures are missing, run `sensors-detect` interactively on the NAS.

For pre-release checks and more detailed diagnostics, see [`docs/operations.md`](docs/operations.md).

## Local development

The repository uses the .NET 10 SDK. From its root:

```bash
dotnet restore NasMonitor.slnx
dotnet test NasMonitor.slnx
dotnet run --project src/NasMonitor.Web/NasMonitor.Web.csproj
```

The base configuration in [`src/NasMonitor.Web/appsettings.json`](src/NasMonitor.Web/appsettings.json) disables hardware and media modules, so the app can start without a NAS. To collect real measurements, use a Linux host with the required tools and configure modules in `/etc/nas-monitor/nas-monitor.json`.
