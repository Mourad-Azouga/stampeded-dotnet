# Changelog

## 0.1.0-preview.1 (unreleased)

- Add C# bindings to Stampeded through its C ABI, with explicit UTF-8 marshalling and SafeHandle ownership.
- Expose typed enqueue requests, pullers, consuming leases, checkpoints and upstream-backed named steps.
- Add dependency injection, scoped C# handlers and a hosted worker with cooperative cancellation.
- Pin upstream sources as a Git submodule and record the source revision in package provenance.
- Build native runtime assets for NuGet with the matching upstream header, schema and BSD license.
- Add native ABI, PostgreSQL integration, hosting and package verification, with platform CI builds.
