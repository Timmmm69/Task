#!/bin/sh
set -eu

: "${OPS02_POSTGRES_CONTAINER:?OPS02_POSTGRES_CONTAINER is required}"
: "${OPS02_ASSET_ROOT:?OPS02_ASSET_ROOT is required}"
: "${OPS02_SOURCE_ROOT:?OPS02_SOURCE_ROOT is required}"

migration_password="$(cut -d: -f5 "$OPS02_ASSET_ROOT/secrets/database/task-migration.pgpass")"
runtime_password="$(cut -d: -f5 "$OPS02_ASSET_ROOT/secrets/database/task-runtime.pgpass")"
bootstrap="$(mktemp)"
trap 'rm -f "$bootstrap"' EXIT

{
    printf "\\set migration_password '%s'\n" "$migration_password"
    printf "\\set runtime_password '%s'\n" "$runtime_password"
    printf "\\set database_name 'task'\n"
    cat "$OPS02_SOURCE_ROOT/deployment/containers/sql/initialize-validation-roles.sql"
} >"$bootstrap"

docker exec -i "$OPS02_POSTGRES_CONTAINER" sh -c '
    passfile="$(mktemp)"
    trap '\''rm -f "$passfile"'\'' EXIT
    printf "*:*:*:postgres:%s\n" "$(cat /run/task-secrets/postgres-admin-password)" >"$passfile"
    chmod 600 "$passfile"
    PGPASSFILE="$passfile" psql -U postgres -d task
' <"$bootstrap"
echo 'OPS02_DATABASE_ROLES_READY'
