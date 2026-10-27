#!/usr/bin/env bash
# Build the local C ABI connector against the published engine for .NET/NuGet.
set -euo pipefail

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)

usage() {
    printf 'Usage: %s [--rid <runtime-identifier>] [--update]\n' "${0##*/}"
    printf 'Build for the current host, or specify a RID with its Rust target and linker installed.\n'
    printf 'Supported RIDs: linux-x64, linux-arm64, win-x64, win-arm64, osx-x64, osx-arm64\n'
    printf 'Use --update to refresh the published engine within the Cargo version requirement.\n'
}

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

rid=
update=()
while (( $# > 0 )); do
    case "$1" in
        --rid)
            (( $# >= 2 )) || fail 'Missing value for --rid.'
            [[ -n "$2" ]] || fail 'Missing value for --rid.'
            rid=$2
            shift 2
            ;;
        --rid=*)
            rid=${1#--rid=}
            [[ -n "$rid" ]] || fail 'Missing value for --rid.'
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        --update)
            update=(--update)
            shift
            ;;
        *)
            fail "Unknown argument: $1. Use --help for usage."
            ;;
    esac
done

if [[ -z "$rid" ]]; then
    case "$(uname -s)" in
        Linux) system=linux ;;
        Darwin) system=osx ;;
        MINGW*|MSYS*|CYGWIN*) system=win ;;
        *) fail 'Unsupported build host. Specify a supported --rid.' ;;
    esac
    case "$(uname -m)" in
        x86_64|amd64|AMD64) architecture=x64 ;;
        aarch64|arm64|ARM64) architecture=arm64 ;;
        *) fail 'Unsupported host architecture. Specify a supported --rid.' ;;
    esac
    rid="$system-$architecture"
fi

case "$rid" in
    linux-x64) triple=x86_64-unknown-linux-gnu; library=libstampeded_ffi.so ;;
    linux-arm64) triple=aarch64-unknown-linux-gnu; library=libstampeded_ffi.so ;;
    win-x64) triple=x86_64-pc-windows-msvc; library=stampeded_ffi.dll ;;
    win-arm64) triple=aarch64-pc-windows-msvc; library=stampeded_ffi.dll ;;
    osx-x64) triple=x86_64-apple-darwin; library=libstampeded_ffi.dylib ;;
    osx-arm64) triple=aarch64-apple-darwin; library=libstampeded_ffi.dylib ;;
    *) fail "Unsupported RID: $rid. Use --help for supported RIDs." ;;
esac

bash "$repository_root/scripts/prepare-native.sh" "${update[@]}"

target_directory=${CARGO_TARGET_DIR:-"$repository_root/artifacts/native-target"}
mkdir -p -- "$target_directory"
target_directory=$(cd -- "$target_directory" && pwd)

(
    cd -- "$repository_root"
    cargo build --locked --release \
        --manifest-path "$repository_root/native/Cargo.toml" \
        -p stampeded-ffi --target "$triple" --target-dir "$target_directory"
)

destination="$repository_root/runtimes/$rid/native"
mkdir -p -- "$destination"
cp -- "$target_directory/$triple/release/$library" "$destination/$library"
printf 'Built %s\n' "$destination/$library"
