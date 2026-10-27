# Changelog

## 0.1.0-preview.1 (unreleased)

- Add C# bindings to Stampeded through its C ABI, with explicit UTF-8 marshalling and SafeHandle ownership.
- Expose typed enqueue requests, pullers, consuming leases, checkpoints and upstream-backed named steps.
- Add dependency injection, scoped C# handlers and a hosted worker with cooperative cancellation.
- Build native runtime assets for NuGet with the matching upstream header, schema and BSD license.
- Add native ABI, PostgreSQL integration, hosting and package verification, with platform CI builds.

### Native dependency migration (2026-10-27)

- Removed the upstream Git submodule. The C ABI connector and header now live in `native/stampeded-ffi/`, while Cargo downloads the published `stampeded` engine crate. Its resolved version and checksum are recorded in `native/Cargo.lock`.
- Updated the native build, Compose setup, tests and NuGet packaging to use the schema from that same resolved crate. Package provenance records the engine version, crate checksum and connector source revision; verification rejects stale or changed native inputs.
- Added `build-native.sh --update` to refresh compatible `0.4.x` engine releases and weekly Cargo dependency update PRs. Existing NuGet packages receive updates when rebuilt and released.
- Validated the Linux x64 native build, four Rust connector checks, nine .NET tests and both NuGet packages. Twenty database, TLS and Mailpit tests were skipped because their services were unavailable; Windows and macOS CI still need a run. See [the validation record](docs/validation.md).
