#!/bin/sh
set -eu

: "${OPS02_POSTGRES_CONTAINER:?OPS02_POSTGRES_CONTAINER is required}"
: "${OPS02_SOURCE_ROOT:?OPS02_SOURCE_ROOT is required}"

docker exec -i "$OPS02_POSTGRES_CONTAINER" sh -c '
    passfile="$(mktemp)"
    trap '\''rm -f "$passfile"'\'' EXIT
    printf "*:*:*:postgres:%s\n" "$(cat /run/task-secrets/postgres-admin-password)" >"$passfile"
    chmod 600 "$passfile"
    PGPASSFILE="$passfile" psql -U postgres -d task
' <"$OPS02_SOURCE_ROOT/deployment/containers/sql/grant-runtime.sql"

echo 'OPS02_RUNTIME_GRANTS_READY'
