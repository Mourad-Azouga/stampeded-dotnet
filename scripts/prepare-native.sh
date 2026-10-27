#!/usr/bin/env bash
# Resolve the published engine and stage its matching schema and provenance.
set -euo pipefail

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
if (( $# > 1 )) || { (( $# == 1 )) && [[ "$1" != --update ]]; }; then
    printf 'Usage: %s [--update]\n' "${0##*/}" >&2
    exit 2
fi

cd -- "$repository_root"
if (( $# == 1 )); then
    cargo update --manifest-path native/Cargo.toml -p stampeded
fi
mkdir -p -- artifacts
metadata=$(mktemp "$repository_root/artifacts/native-metadata.XXXXXX")
trap 'rm -f -- "$metadata"' EXIT
cargo metadata --locked --manifest-path native/Cargo.toml --format-version 1 > "$metadata"
dotnet run --project tools/Stampeded.Verify/Stampeded.Verify.csproj \
    --configuration Release --verbosity quiet -- prepare-upstream "$repository_root" "$metadata"
