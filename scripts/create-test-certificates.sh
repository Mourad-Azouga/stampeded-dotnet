#!/usr/bin/env bash
# Create a private CA and localhost server certificate for disposable PostgreSQL tests.
set -euo pipefail

if (( $# != 1 )); then
    printf 'Usage: %s <output-directory>\n' "${0##*/}" >&2
    exit 2
fi
mkdir -p -- "$1"
certificate_directory=$(cd -- "$1" && pwd)

openssl req -x509 -newkey rsa:2048 -nodes -days 2 \
    -subj '/CN=Stampeded Test CA' \
    -keyout "$certificate_directory/ca.key" -out "$certificate_directory/ca.crt" \
    -addext 'basicConstraints=critical,CA:TRUE' \
    -addext 'keyUsage=critical,keyCertSign,cRLSign'
openssl req -newkey rsa:2048 -nodes -subj '/CN=localhost' \
    -keyout "$certificate_directory/server.key" -out "$certificate_directory/server.csr"
cat > "$certificate_directory/server.ext" <<'EXTENSIONS'
subjectAltName=DNS:localhost,IP:127.0.0.1
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
EXTENSIONS
openssl x509 -req -in "$certificate_directory/server.csr" \
    -CA "$certificate_directory/ca.crt" -CAkey "$certificate_directory/ca.key" \
    -CAcreateserial -days 2 -extfile "$certificate_directory/server.ext" \
    -out "$certificate_directory/server.crt"
chmod 600 "$certificate_directory/ca.key" "$certificate_directory/server.key"
printf 'Created test certificates in %s\n' "$certificate_directory"
