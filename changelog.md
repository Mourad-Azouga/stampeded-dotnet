# Changelog

## 0.1.0

- Add C# bindings to Stampeded through its C ABI, with explicit UTF-8 marshalling and SafeHandle ownership.
- Expose typed enqueue requests, pullers, consuming leases, checkpoints and upstream-backed named steps.
- Add dependency injection, scoped C# handlers and a hosted worker with cooperative cancellation.
- Build native runtime assets for NuGet with the matching upstream header, schema and BSD license.
- Add native ABI, PostgreSQL integration, hosting and package verification, with platform CI builds.

### 0.1.1

- Removed the upstream Git submodule. The C ABI connector and header now live in `native/stampeded-ffi/`, while Cargo downloads the published `stampeded` engine crate. Its resolved version and checksum are recorded in `native/Cargo.lock`.
- Updated the native build, Compose setup, tests and NuGet packaging to use the schema from that same resolved crate. Package provenance records the engine version, crate checksum and connector source revision; verification rejects stale or changed native inputs.
- Added `build-native.sh --update` to refresh compatible `0.4.x` engine releases and weekly Cargo dependency update PRs. Existing NuGet packages receive updates when rebuilt and released.
- Validated the Linux x64 native build, four Rust connector checks, nine .NET tests and both NuGet packages.
### 0.1.2

- Switched the native binding to the published `stampeded-ffi` 0.4.1 crate and removed the repository's copied Rust C ABI connector. C# P/Invoke now loads a shared library built directly from that crate; its exported signatures remain compatible with the binding.
- Pinned the published FFI archive by SHA-256 and its core/dependencies in `native/Cargo.lock`. Preparation stages the crate's C header, the core's numbered SQL migrations, a fresh-install script with migration records, and package provenance.
- Updated native builds, CI, database setup, compatibility documentation and package verification for the published FFI and schema version 5. Existing databases still require the matching upstream daemon's `--migrate`.
- Validated the Linux x64 native build, five applicable published FFI tests, nine .NET tests, formatting and both NuGet packages. 
- Still need to test on mailkit or smtn
