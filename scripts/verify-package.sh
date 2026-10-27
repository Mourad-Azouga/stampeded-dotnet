#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
if (( $# != 1 )); then
    printf 'Usage: %s <core-package.nupkg>\n' "${0##*/}" >&2
    exit 2
fi

exec dotnet run --project "$repository_root/tools/Stampeded.Verify/Stampeded.Verify.csproj" \
    --configuration Release --verbosity quiet -- package "$repository_root" "$1"
