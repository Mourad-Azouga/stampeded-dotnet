# Validation record

## Submodule checkout verified on 2026-10-22

Local environment: .NET SDK 10.0.113, Rust 1.99.0, PostgreSQL 18.6, Linux x64.
The native library was built from upstream commit `53bc32de1c5fc4cb84f8123c1871d6738f48c62d`.

- Cloned the .NET repository into a fresh directory with `--recurse-submodules`, fetching upstream
  directly from GitHub. Confirmed that `native/upstream` is a gitlink at the recorded revision.
- Built the actual upstream `stampeded-ffi` crate in release mode with its unchanged Cargo.lock.
- Verified the Bash build entry point with `bash -n` and a real Linux x64 native build. Checked RID
  mappings, simulated host detection, argument failures, paths with spaces and Cargo failure
  propagation. These shell checks do not establish native execution on other platforms.
- Verified that the source checker rejects an uninitialized submodule, a wrong checked-out commit,
  modified upstream files and a mismatch between the gitlink and package provenance.
- Built the .NET solution from clean outputs with **zero warnings and zero errors**.
- Verified C# formatting with `dotnet format --verify-no-changes --no-restore`.
- Ran the full suite against the real native library and disposable PostgreSQL instance:
  **24 passed, 0 failed, 0 skipped**.
- Created both NuGet preview packages and checked runtime paths, upstream revision, header, schema,
  license and the absence of a production Npgsql dependency.
- Restored packages into a separate consumer with a fresh package cache, without project references
  or `STAMPEDED_NATIVE_LIBRARY`. Loaded the native library through NuGet runtime assets, enqueued a
  Unicode payload and executed a scoped C# handler with an upstream-backed named step. PostgreSQL
  confirmed `completed`, attempt `1` and checkpoint sequence `1`.
- Checked tracked changes with `git diff --check`.

The local packages contain the Linux x64 native asset only. NuGet publishing is not configured.

## Previous cross-platform CI

[GitHub Actions run 39775979253](https://github.com/Mourad-Azouga/stampeded-dotnet/actions/runs/39775979253)
passed on commit `ac71e577407ad179e6ff0fac638ec58a137cb751` before the submodule restructuring:

- Linux x64, Windows x64 and macOS ARM64: native build, .NET build, formatting and ABI tests.
- Linux with PostgreSQL 18: integration tests, combined-platform packaging and package verification.

The updated workflow fetches submodules in both jobs. Its cross-platform execution must be verified
again after pushing the restructured repository; the previous run does not verify that change.

## Test coverage

ABI tests exercise version loading, native struct layout, native error conversion, embedded NUL
rejection, upstream step-record round trips and disposal. Database tests cover enqueue/pull/complete,
Unicode payloads, unique scopes, retries and attempt exhaustion, resume state, persisted named-step
results, stale ownership, interruption, requeue, discard, scheduling, metadata and priority. Lifetime
checks exercise a puller surviving client disposal, repeated disposal, outcome failures and concurrent
attempts to consume one lease. A consumed lease also rejects a later step before application work runs.

Hosting tests cover per-attempt handler scopes and disposal, retries, unknown-task discard,
cooperative timeouts, shutdown cancellation/requeue and duplicate client registration.
Npgsql appears only in the test harness for isolated schema setup and result assertions. Production
enqueue, pull, checkpoint and outcome operations in these tests go through the upstream C ABI.

## Not yet verified

- Real SMTP/provider email delivery or real PDF rendering and file persistence.
- Process-kill recovery with the upstream daemon, cross-language workers, load, failover and soak tests.
- NativeAOT, trimming, musl/Alpine, additional runtime assets or older .NET targets.

The sample prepares and logs text only. No packages have been published and no production deployment
is claimed by this record.
