#!/usr/bin/env bash
# Fetch the published C ABI crate and stage its matching header, license and schema.
set -euo pipefail

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
if (( $# > 1 )) || { (( $# == 1 )) && [[ "$1" != --update ]]; }; then
    printf 'Usage: %s [--update]\n' "${0##*/}" >&2
    exit 2
fi

destination="$repository_root/artifacts/native-source"
mkdir -p -- "$destination"
if (( $# == 1 )); then
    index=$(mktemp "$destination/ffi-index.XXXXXX")
    trap 'rm -f -- "$index"' EXIT
    curl --fail --location --retry 3 --silent --show-error \
        'https://index.crates.io/st/am/stampeded-ffi' -o "$index"
    dotnet run --project "$repository_root/tools/Stampeded.Verify/Stampeded.Verify.csproj" \
        --configuration Release --verbosity quiet -- refresh-ffi "$repository_root" "$index"
    rm -f -- "$index"
fi
version=$(dotnet run --project "$repository_root/tools/Stampeded.Verify/Stampeded.Verify.csproj" --configuration Release --verbosity quiet -- ffi-version "$repository_root")
checksum=$(dotnet run --project "$repository_root/tools/Stampeded.Verify/Stampeded.Verify.csproj" --configuration Release --verbosity quiet -- ffi-checksum "$repository_root")
archive="$destination/stampeded-ffi-$version.crate"
if [[ -n "${STAMPEDED_FFI_ARCHIVE:-}" ]]; then
    cp -- "$STAMPEDED_FFI_ARCHIVE" "$archive"
elif [[ ! -f "$archive" ]]; then
    curl --fail --location --retry 3 --silent --show-error \
        "https://static.crates.io/crates/stampeded-ffi/stampeded-ffi-$version.crate" -o "$archive"
fi
if command -v sha256sum >/dev/null 2>&1; then
    actual=$(sha256sum "$archive")
else
    actual=$(shasum -a 256 "$archive")
fi
[[ "${actual%% *}" == "$checksum" ]] || { printf 'Published FFI archive checksum mismatch.\n' >&2; exit 1; }

crate="$destination/crate"
rm -rf -- "$crate"
mkdir -p -- "$crate"
tar -xzf "$archive" --strip-components=1 -C "$crate"
if (( $# == 1 )); then
    cargo update --manifest-path "$crate/Cargo.toml"
    cp -- "$crate/Cargo.lock" "$repository_root/native/Cargo.lock"
else
    cp -- "$repository_root/native/Cargo.lock" "$crate/Cargo.lock"
fi
metadata=$(mktemp "$destination/native-metadata.XXXXXX")
trap 'rm -f -- "$metadata"' EXIT
cargo metadata --locked --manifest-path "$crate/Cargo.toml" --format-version 1 > "$metadata"
dotnet run --project "$repository_root/tools/Stampeded.Verify/Stampeded.Verify.csproj" \
    --configuration Release --verbosity quiet -- prepare-upstream "$repository_root" "$metadata"
