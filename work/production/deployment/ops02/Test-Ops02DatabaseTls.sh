#!/bin/sh
set -eu

: "${OPS02_NETWORK:?OPS02_NETWORK is required}"
: "${OPS02_ASSET_ROOT:?OPS02_ASSET_ROOT is required}"
: "${OPS02_POSTGRES_IMAGE:?OPS02_POSTGRES_IMAGE is required}"

verified="$(docker run --rm --pull never --network "$OPS02_NETWORK" --entrypoint sh \
    -v "$OPS02_ASSET_ROOT/secrets/database:/secrets:ro" "$OPS02_POSTGRES_IMAGE" -c \
    'export PGPASSFILE=/secrets/task-runtime.pgpass PGSSLMODE=verify-full PGSSLROOTCERT=/secrets/postgres-ca.pem; psql -h postgres -p 5432 -U task_runtime -d task -Atc "select ssl, version from pg_stat_ssl where pid=pg_backend_pid()"')"
case "$verified" in
    t\|TLSv1.2|t\|TLSv1.3) ;;
    *) echo "unexpected verified TLS result: $verified" >&2; exit 1 ;;
esac

set +e
docker run --rm --pull never --network "$OPS02_NETWORK" --entrypoint sh \
    -v "$OPS02_ASSET_ROOT/secrets/database:/secrets:ro" "$OPS02_POSTGRES_IMAGE" -c \
    'export PGPASSFILE=/secrets/task-runtime.pgpass PGSSLMODE=disable; psql -h postgres -p 5432 -U task_runtime -d task -Atc "select 1"' \
    >/tmp/ops02-plaintext.out 2>&1
plain_code=$?
set -e
if [ "$plain_code" -eq 0 ]; then
    echo 'plaintext PostgreSQL probe unexpectedly succeeded' >&2
    rm -f /tmp/ops02-plaintext.out
    exit 1
fi
if ! grep -q 'pg_hba.conf rejects connection.*no encryption' /tmp/ops02-plaintext.out; then
    cat /tmp/ops02-plaintext.out >&2
    rm -f /tmp/ops02-plaintext.out
    exit 1
fi
rm -f /tmp/ops02-plaintext.out
printf 'OPS02_DATABASE_TLS_OK verified=%s plaintextRejected=true\n' "$verified"
