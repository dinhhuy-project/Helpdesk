#!/bin/sh
set -eu

fail() { printf '%s\n' "$1" >&2; exit 1; }
run() { "$@" >/dev/null 2>&1 || fail 'MinIO application verification failed; check credentials and scoped policy.'; }
umask 077
verification_dir=$(mktemp -d /tmp/helpdesk-minio-verify.XXXXXX)
export MC_CONFIG_DIR="$verification_dir"
probe=''
outside=''
cleanup() {
    if [ -n "$probe" ]; then mc rm "$probe" >/dev/null 2>&1 || true; fi
    # Only paths created by this verification, never existing objects or user-provided directories.
    if [ "${outside_created:-false}" = true ]; then mc rm "$outside" >/dev/null 2>&1 || true; fi
    rm -rf -- "$verification_dir"
}
trap cleanup EXIT
trap 'exit 1' HUP INT TERM
run mc alias set application "${MINIO_SETUP_ENDPOINT:-http://minio:9000}" "$MINIO_APP_ACCESS_KEY" "$MINIO_APP_SECRET_KEY"
run mc ls application/helpdesk-attachments
policy=$(mc anonymous get-json application/helpdesk-attachments 2>/dev/null) || fail 'Application cannot inspect bucket privacy.'
policy=$(printf '%s' "$policy" | tr -d '[:space:]')
case "$policy" in null|'{}'|'') ;; *) fail 'Bucket is not private.' ;; esac
versioning=$(mc --no-color version info application/helpdesk-attachments 2>/dev/null) || fail 'Application cannot inspect versioning.'
case "$versioning" in *' is un-versioned') ;; *) fail 'Bucket is versioned.' ;; esac

probe_id=$(cat /proc/sys/kernel/random/uuid)
probe="application/helpdesk-attachments/attachments/setup-check-$probe_id.txt"
outside="application/helpdesk-attachments/outside-prefix-check-$probe_id.txt"
printf '%s' 'Helpdesk private storage verification' > "$verification_dir/probe.txt"
run mc cp "$verification_dir/probe.txt" "$probe"
downloaded=$(mc --no-color cat "$probe" 2>/dev/null) || fail 'Application download failed.'
[ "$downloaded" = 'Helpdesk private storage verification' ] || fail 'Downloaded content differs.'
if mc cp "$verification_dir/probe.txt" "$outside" >/dev/null 2>&1; then
    outside_created=true
    fail 'Application can write outside the attachments prefix; review policy.'
fi
if mc admin user list application >/dev/null 2>&1; then fail 'Application unexpectedly has administrator access.'; fi
run mc rm "$probe"
printf '%s\n' 'Verified: private/unversioned bucket, application upload/download/delete, prefix restriction and denied administrator access.'
