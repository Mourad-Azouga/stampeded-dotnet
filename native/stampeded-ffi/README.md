# C ABI connector

This is the upstream `stampeded-ffi` connector retained in the .NET repository because it is
not published on crates.io. Its Rust sources and C header were copied unchanged from the
revision recorded as `ffiSourceCommit` in `../upstream.json`. The upstream BSD-3-Clause
license is retained in `LICENSE`.

The connector delegates to the published `stampeded` crate with its default features disabled;
it selects the AWS-LC TLS backend. It contains no copy of the engine or SQL schema.
The .NET P/Invoke declarations call this connector's C exports.

Run `bash scripts/build-native.sh` from the repository root. `--update` refreshes the published
core within the manifest's `0.4` compatibility range. Commit the resulting `native/Cargo.lock`
after validation. A new incompatible core release requires reviewing the connector and changing
the version requirement. Updates to the connector itself are reviewed separately against upstream.
