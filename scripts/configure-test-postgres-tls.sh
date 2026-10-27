#!/usr/bin/env bash
# Enable private-CA TLS in the disposable PostgreSQL container used by the test suite.
set -euo pipefail

if (( $# != 2 )) || [[ -z "$1" ]]; then
    printf 'Usage: %s <postgres-container-id> <certificate-directory>\n' "${0##*/}" >&2
    exit 2
fi
repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
postgres_container=$1
bash "$repository_root/scripts/create-test-certificates.sh" "$2"
certificate_directory=$(cd -- "$2" && pwd)

docker cp "$certificate_directory/server.crt" "$postgres_container:/tmp/stampeded-server.crt"
docker cp "$certificate_directory/server.key" "$postgres_container:/tmp/stampeded-server.key"
docker exec --user root "$postgres_container" chown postgres:postgres /tmp/stampeded-server.crt /tmp/stampeded-server.key
docker exec --user root "$postgres_container" chmod 600 /tmp/stampeded-server.key
docker exec "$postgres_container" psql -U stampeded -d stampeded -v ON_ERROR_STOP=1 \
    -c "ALTER SYSTEM SET ssl = 'on'" \
    -c "ALTER SYSTEM SET ssl_cert_file = '/tmp/stampeded-server.crt'" \
    -c "ALTER SYSTEM SET ssl_key_file = '/tmp/stampeded-server.key'" \
    -c "SELECT pg_reload_conf()"
printf 'PostgreSQL TLS configured. Test root CA: %s/ca.crt\n' "$certificate_directory"
