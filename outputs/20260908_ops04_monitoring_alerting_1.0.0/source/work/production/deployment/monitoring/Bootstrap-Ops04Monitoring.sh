#!/bin/sh
set -eu

: "${OPS04_POSTGRES_CONTAINER:?OPS04_POSTGRES_CONTAINER is required}"
: "${TASK_SECRET_ROOT:?TASK_SECRET_ROOT is required}"
: "${TASK_MONITORING_GID:?TASK_MONITORING_GID is required}"
: "${TASK_DB_NAME:?TASK_DB_NAME is required}"

case "$TASK_MONITORING_GID" in *[!0-9]*|'') echo 'TASK_MONITORING_GID must be numeric' >&2; exit 1;; esac
case "$TASK_DB_NAME" in *[!A-Za-z0-9_]*|'') echo 'TASK_DB_NAME must contain only letters, digits, or underscores' >&2; exit 1;; esac

monitoring_dir="$TASK_SECRET_ROOT/monitoring"
user_file="$monitoring_dir/postgres-user"
password_file="$monitoring_dir/postgres-password"
token_file="$monitoring_dir/metrics-token"
mkdir -p "$monitoring_dir"

if [ ! -f "$user_file" ]; then printf '%s\n' 'task_monitoring' >"$user_file"; fi
if [ ! -f "$password_file" ]; then umask 077; openssl rand -hex 32 >"$password_file"; fi
if [ ! -f "$token_file" ]; then umask 077; openssl rand -hex 32 >"$token_file"; fi

[ "$(cat "$user_file")" = 'task_monitoring' ] || { echo 'Unexpected monitoring database role' >&2; exit 1; }
grep -Eq '^[0-9a-f]{64}$' "$password_file" || { echo 'Monitoring password must be 32-byte random hex' >&2; exit 1; }
grep -Eq '^[0-9a-f]{64}$' "$token_file" || { echo 'Metrics token must be 32-byte random hex' >&2; exit 1; }
chgrp "$TASK_MONITORING_GID" "$user_file" "$password_file" "$token_file"
chmod 640 "$user_file" "$password_file" "$token_file"

bootstrap="$(mktemp)"
trap 'rm -f "$bootstrap"' EXIT
{
  printf "\\set monitoring_password '%s'\n" "$(cat "$password_file")"
  cat <<'SQL'
SELECT format('CREATE ROLE task_monitoring LOGIN PASSWORD %L', :'monitoring_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'task_monitoring') \gexec
ALTER ROLE task_monitoring LOGIN PASSWORD :'monitoring_password';
GRANT CONNECT ON DATABASE :"monitoring_database" TO task_monitoring;
GRANT pg_monitor TO task_monitoring;
SQL
} >"$bootstrap"

docker exec -e "TASK_DB_NAME=$TASK_DB_NAME" -i "$OPS04_POSTGRES_CONTAINER" sh -c '
  passfile="$(mktemp)"
  trap '\''rm -f "$passfile"'\'' EXIT
  printf "*:*:*:postgres:%s\n" "$(cat /run/task-secrets/postgres-admin-password)" >"$passfile"
  chmod 600 "$passfile"
  PGPASSFILE="$passfile" psql -X -v ON_ERROR_STOP=1 -v monitoring_database="$TASK_DB_NAME" -U postgres -d "$TASK_DB_NAME"
' <"$bootstrap"

echo 'OPS04_MONITORING_IDENTITY_READY'
