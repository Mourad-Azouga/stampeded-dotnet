#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
if (( $# != 0 )); then
    printf 'Usage: %s\n' "${0##*/}" >&2
    exit 2
fi

exec dotnet run --project "$repository_root/tools/Stampeded.Verify/Stampeded.Verify.csproj" \
    --configuration Release --verbosity quiet -- upstream "$repository_root"
