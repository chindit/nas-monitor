#!/usr/bin/env bash
set -Eeuo pipefail

if [[ ${EUID} -ne 0 ]]; then
    echo "Run this deployment script as root." >&2
    exit 1
fi
if [[ $# -ne 2 ]]; then
    echo "Usage: $0 VERSION ARCHIVE.tar.gz" >&2
    exit 64
fi

version=$1
archive=$(realpath -- "$2")
checksum=$(realpath -- "${archive}.sha256")
releases_root=/opt/nas-monitor/releases

if [[ -z ${version} || ! ${version} =~ ^[A-Za-z0-9._-]+$ ]]; then
    echo "Invalid release version." >&2
    exit 64
fi
destination=$(realpath -m -- "${releases_root}/${version}")
case "${destination}" in
    "${releases_root}/"*) ;;
    *) echo "Release destination is outside ${releases_root}." >&2; exit 64 ;;
esac
if [[ ${destination} == / || ${destination} == /opt/nas-monitor || ${destination} == ${releases_root} || -e ${destination} ]]; then
    echo "Unsafe or existing release destination." >&2
    exit 64
fi

expected_hash=$(awk 'NR == 1 { print $1 }' "${checksum}")
if [[ ! ${expected_hash} =~ ^[A-Fa-f0-9]{64}$ ]]; then
    echo "Invalid SHA-256 checksum file." >&2
    exit 65
fi
printf '%s  %s\n' "${expected_hash}" "${archive}" | sha256sum --check --status

install -d -o root -g root -m 0755 "${destination}"
tar -xzf "${archive}" -C "${destination}" --strip-components=1
if [[ ! -x ${destination}/tools/nas-monitor-smart-collector || ! -x ${destination}/web/NasMonitor.Web ]]; then
    echo "The release archive has an invalid layout." >&2
    exit 65
fi
chown -R root:root "${destination}"
chmod -R g-w,o-w "${destination}"
install -o root -g root -m 0755 "${destination}/tools/nas-monitor-smart-collector" /usr/local/libexec/nas-monitor-smart-collector

previous_target=''
if [[ -L /opt/nas-monitor/current ]]; then
    previous_target=$(readlink -f -- /opt/nas-monitor/current)
fi
temporary_link="/opt/nas-monitor/.current-${version}-$$"
cleanup() { rm -f -- "${temporary_link}"; }
trap cleanup EXIT
ln -s -- "${destination}/web" "${temporary_link}"
mv -Tf -- "${temporary_link}" /opt/nas-monitor/current

systemctl restart nas-monitor.service
healthy=false
for _ in $(seq 1 30); do
    if curl --fail --silent --show-error --max-time 1 http://127.0.0.1:5180/health/live >/dev/null; then
        healthy=true
        break
    fi
    sleep 1
done

if [[ ${healthy} != true ]]; then
    echo "Health check failed; rolling back." >&2
    if [[ -n ${previous_target} ]]; then
        rollback_link="/opt/nas-monitor/.rollback-$$"
        ln -s -- "${previous_target}" "${rollback_link}"
        mv -Tf -- "${rollback_link}" /opt/nas-monitor/current
        systemctl restart nas-monitor.service
    else
        systemctl stop nas-monitor.service
    fi
    exit 1
fi

systemctl enable nas-monitor.service
echo "Release ${version} deployed successfully."
