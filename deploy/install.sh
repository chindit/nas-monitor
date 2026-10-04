#!/usr/bin/env bash
set -Eeuo pipefail

if [[ ${EUID} -ne 0 ]]; then
    echo "Run this installer as root." >&2
    exit 1
fi

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
repository_dir=$(cd -- "${script_dir}/.." && pwd -P)

for command in vmstat free uptime sensors lsblk findmnt zpool zfs systemctl sudo smartctl visudo; do
    command -v "${command}" >/dev/null || { echo "Missing required command: ${command}" >&2; exit 1; }
done

if ! id nas-monitor >/dev/null 2>&1; then
    useradd --system --no-create-home --home-dir /nonexistent --shell /usr/bin/nologin nas-monitor
fi

install -d -o root -g root -m 0755 /etc/nas-monitor /opt/nas-monitor /opt/nas-monitor/releases /usr/local/libexec
if [[ ! -e /etc/nas-monitor/nas-monitor.json ]]; then
    install -o root -g nas-monitor -m 0640 "${repository_dir}/config/nas-monitor.example.json" /etc/nas-monitor/nas-monitor.json
fi
if [[ ! -e /etc/nas-monitor/nas-monitor.env ]]; then
    install -o root -g root -m 0600 "${repository_dir}/config/nas-monitor.env.example" /etc/nas-monitor/nas-monitor.env
fi

visudo -cf "${script_dir}/nas-monitor.sudoers"
install -o root -g root -m 0440 "${script_dir}/nas-monitor.sudoers" /etc/sudoers.d/nas-monitor
install -o root -g root -m 0644 "${script_dir}/nas-monitor.service" /etc/systemd/system/nas-monitor.service
systemd-analyze verify /etc/systemd/system/nas-monitor.service
systemctl daemon-reload

echo "Base installation complete. Edit /etc/nas-monitor files, then deploy a release archive."
