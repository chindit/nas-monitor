# NAS Monitor operations

## Prerequisites

Install the required Arch Linux packages and confirm every executable path:

```bash
pacman -S --needed smartmontools lm_sensors sudo
command -v vmstat free uptime sensors lsblk findmnt zpool zfs systemctl sudo smartctl
```

Run `deploy/install.sh` as root, edit `/etc/nas-monitor/nas-monitor.json`, and replace both token placeholders in `/etc/nas-monitor/nas-monitor.env`. Keep the environment file mode `0600` and owned by root.

Generate the Caddy password hash interactively with `caddy hash-password --algorithm argon2id`, adapt `deploy/Caddyfile.example`, then run:

```bash
caddy validate --config /etc/caddy/Caddyfile
systemctl reload caddy
```

Do not expose TCP port 5180. Confirm that Kestrel is listening only on `127.0.0.1:5180`.

## Deployment

Download the versioned archive and adjacent `.sha256` file from CI, copy both to the NAS, and run:

```bash
sudo deploy/deploy.sh 1.0.0 /path/to/nas-monitor-1.0.0.tar.gz
```

The script verifies the checksum, installs an immutable release, updates the current symlink atomically, and restores the previous release if the liveness check fails.

## Diagnostics

```bash
systemctl status nas-monitor.service
journalctl -u nas-monitor.service
curl http://127.0.0.1:5180/health/live
sudo -u nas-monitor sudo -n -- /usr/local/libexec/nas-monitor-smart-collector
```

Configuration changes require `systemctl restart nas-monitor.service`. If temperature data is absent, run `sensors-detect` interactively; the installer deliberately does not probe hardware.

## Release acceptance checklist

Before declaring a NAS deployment complete, verify all of the following on the real host:

1. `ss -ltnp` shows port 5180 bound only to `127.0.0.1`.
2. Port 5180 is unreachable from another LAN machine.
3. Caddy provides a valid HTTPS certificate and rejects missing or invalid credentials.
4. The dashboard loads after authentication and one click on Refresh causes one collection.
5. SMART lists every physical disk, including the system disk and all pool members.
6. ZFS reports pool `medias`, its expected vdevs, scan state, and zero/non-zero errors accurately.
7. Plex and Jellyfin sessions match their native dashboards.
8. Stopping either media service affects only its corresponding card.
9. Temporarily denying the sudo rule makes only SMART unavailable.
10. Module errors expose neither stack traces, command output, tokens, nor media paths.
11. `systemctl restart nas-monitor.service` succeeds.
12. A deliberately invalid test release triggers the deployment rollback path.

Old directories under `/opt/nas-monitor/releases` are intentionally retained. Remove them only as a separate, explicit administrative operation after confirming they are not the current or rollback target.
