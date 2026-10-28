#!/bin/sh
set -eu
umask 077

ready_file=/tmp/helpdesk-minio-ready
server_pid=''
bootstrap_dir=''
cleanup() {
    rm -f "$ready_file"
    if [ -n "$server_pid" ] && kill -0 "$server_pid" 2>/dev/null; then
        kill -TERM "$server_pid" 2>/dev/null || true
        wait "$server_pid" || true
    fi
    # This directory is created by mktemp below, never accepted from configuration.
    if [ -n "$bootstrap_dir" ]; then rm -rf -- "$bootstrap_dir"; fi
}
trap cleanup EXIT
trap 'exit 143' TERM
trap 'exit 130' INT

# Container writable layers survive restart. A previous ready marker must not bypass setup.
rm -f "$ready_file"
minio "$@" &
server_pid=$!
server_ready=false
attempt=0
while [ "$attempt" -lt 60 ]; do
    if ! kill -0 "$server_pid" 2>/dev/null; then break; fi
    if curl --fail --silent --output /dev/null --connect-timeout 1 --max-time 2 http://127.0.0.1:9000/minio/health/live; then
        server_ready=true
        break
    fi
    attempt=$((attempt + 1))
    sleep 1
done
if [ "$server_ready" != true ]; then
    printf '%s\n' 'MinIO did not become ready for attachment setup.' >&2
    exit 1
fi

bootstrap_dir=$(mktemp -d /tmp/helpdesk-minio-bootstrap.XXXXXX)
export MC_CONFIG_DIR="$bootstrap_dir"
export MINIO_SETUP_ENDPOINT=http://127.0.0.1:9000
export MINIO_SETUP_POLICY=/opt/helpdesk/attachments-policy.json
/bin/sh /opt/helpdesk/initialize.sh
rm -rf -- "$bootstrap_dir"
bootstrap_dir=''
touch "$ready_file"
# PID 1 forwards stop signals, reaps MinIO and returns its exit status.
wait "$server_pid"
