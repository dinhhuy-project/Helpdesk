#!/bin/sh
set -eu

fail() { printf '%s\n' "$1" >&2; exit 1; }
# Avoid raw CLI output: errors may contain account names, endpoint or credentials.
run() { "$@" >/dev/null 2>&1 || fail 'MinIO setup failed. Check credentials, account policy and service readiness.'; }

case "${MINIO_ENABLED:-false}" in
    true|True|TRUE) ;;
    false|False|FALSE) printf '%s\n' 'Attachment storage is disabled; setup skipped.'; exit 0 ;;
    *) fail 'MINIO_ENABLED must be true or false.' ;;
esac
[ -n "${MINIO_APP_ACCESS_KEY:-}" ] && [ -n "${MINIO_APP_SECRET_KEY:-}" ] || fail 'Set both MINIO_APP credentials before enabling storage.'
[ "$MINIO_APP_ACCESS_KEY" != "$MINIO_ROOT_USER" ] || fail 'The application account must differ from the root account.'
[ "${#MINIO_APP_SECRET_KEY}" -ge 32 ] || fail 'Use an application secret of at least 32 characters.'

run mc alias set bootstrap "${MINIO_SETUP_ENDPOINT:-http://minio:9000}" "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD"
# A newly created bucket is private and unversioned. Never overwrite an existing bucket policy/versioning.
run mc mb --ignore-existing bootstrap/helpdesk-attachments
bucket_policy=$(mc anonymous get-json bootstrap/helpdesk-attachments 2>/dev/null) || fail 'Cannot verify the attachment bucket policy.'
bucket_policy=$(printf '%s' "$bucket_policy" | tr -d '[:space:]')
case "$bucket_policy" in
    null|'{}'|'') ;;
    *) fail 'The attachment bucket has an existing policy. Setup refuses to change it; use a private bucket.' ;;
esac
versioning=$(mc --no-color version info bootstrap/helpdesk-attachments 2>/dev/null) || fail 'Cannot verify bucket versioning.'
case "$versioning" in
    *' is un-versioned') ;;
    *) fail 'The attachment bucket must never have enabled versioning. Existing settings were preserved.' ;;
esac
run mc admin policy create bootstrap helpdesk-attachments-app "${MINIO_SETUP_POLICY:-/setup/attachments-policy.json}"
if ! mc admin user info bootstrap "$MINIO_APP_ACCESS_KEY" >/dev/null 2>&1; then
    run mc admin user add bootstrap "$MINIO_APP_ACCESS_KEY" "$MINIO_APP_SECRET_KEY"
fi
run mc admin policy attach bootstrap helpdesk-attachments-app --user "$MINIO_APP_ACCESS_KEY"
run mc alias set application "${MINIO_SETUP_ENDPOINT:-http://minio:9000}" "$MINIO_APP_ACCESS_KEY" "$MINIO_APP_SECRET_KEY"
run mc ls application/helpdesk-attachments
printf '%s\n' 'Attachment bucket and scoped application account are ready. Existing passwords and bucket settings were preserved.'
