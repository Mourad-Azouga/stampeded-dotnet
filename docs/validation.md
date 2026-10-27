# Validation record

## Published-crate build verified on 2026-10-27

The active build now retains the C ABI connector in `native/stampeded-ffi/` and resolves the
engine from crates.io using `native/Cargo.lock`. The schema and package provenance are prepared
from that resolved crate. The submodule results below are historical evidence; they do not
establish validation of this build path or of a future crate update.

Local environment: Linux x64, .NET SDK 10.0.113, Rust 1.99.0. The resolved engine is the
published `stampeded` 0.4.0 crate with checksum
`d727727ed6452f93377eeffc74ce59c3b569cb1d35b5489800ca3d4e640f32dd`.

- Built the real native library through `build-native.sh --update` and the .NET solution with
  zero warnings/errors. The update resolved the existing 0.4.0 release without changing the lockfile.
- Passed all four Rust connector checks: generated C header, lifecycle, cron and step records.
- Passed nine .NET tests with zero failures; twenty PostgreSQL/TLS/Mailpit cases skipped because
  their local services and settings were unavailable. The Docker executable points to an unavailable
  Docker Desktop installation in this environment.
- Created both NuGet packages and verified the Linux native asset, resolved crate provenance,
  Cargo lockfile, header, matching schema, license and absence of a production Npgsql dependency.
- Confirmed the retained connector sources and header are byte-for-byte copies of their origin.
- Accepted a clean source fixture and rejected changed lockfile/header/schema fixtures and a
  non-registry engine dependency, each with exit code 1.
- Passed Rust/C# formatting, Bash syntax and Git whitespace checks. No tracked Git submodule remains.

The updated Windows/macOS workflow and database/workload checks still need a fresh CI run.

## Upstream 0.4.0 and workload tests verified on 2026-10-25

Pinned upstream commit: `d84c19ff302688c3d0a997b7be9a0b4f25e4ce8a`, core 0.4.0, C ABI 0.1.0,
schema version 3. Local environment: .NET SDK 10.0.113, Rust 1.99.0, PostgreSQL 18.6, Mailpit 1.32.0,
Linux x64. The build used upstream's default AWS-LC TLS provider and unchanged Cargo.lock.

- Built the native library and the .NET solution with zero warnings/errors; NuGet audit passed.
- Ran the complete .NET suite: **29 passed, 0 failed, 0 skipped**, including all earlier binding
  tests, two schema regressions, private-CA TLS with untrusted-CA rejection, and two PDF/SMTP cases.
- Ran upstream's five real schema migration tests with `CI=1` and a configured database:
  **5 passed**, covering concurrent/repeated starts, existing 0.3 upgrades and invalid schema versions.
- Generated and parsed a one-page PDF, delivered its exact bytes as an attachment through SMTP,
  retrieved the captured MIME message from Mailpit, and checked recipient, Unicode body and job state.
- A separate NuGet consumer with a fresh cache, no project references and no native path override
  connected over verified TLS and persisted `completed|1|1` (state, attempt, checkpoint sequence).
- The controlled retry used two attempts, rendered the PDF once and captured one email. It failed
  before SMTP delivery, so it does not establish exactly-once delivery after an ambiguous acknowledgement.
- Reproduced a pre-existing upstream deduplication collision for the distinct scopes `a` and `\x61`
  on both old and new schemas. See [the review and SQL reproducer](upstream-0.4-review.md).

The Docker-based test bootstrap and updated Windows/macOS jobs still require a fresh workflow run.
The older platform results below do not verify the new TLS provider. No packages were published.

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

## Verification tooling verified on 2026-10-22

All three developer entry points now use Bash and run without Python. The verification entry points
invoke the non-packable `tools/Stampeded.Verify` C# tool, which has no external package dependencies.

- Passed Bash syntax checks and built the real Linux x64 native library through the new entry points.
- Built the full solution, including the verifier, with zero warnings/errors and passed formatting.
- Created both NuGet packages and verified native assets, README, header, schema, license and provenance.
- Rejected eight invalid source/provenance fixtures and thirteen corrupt/incomplete package fixtures,
  including modified binaries, missing licenses, malformed XML/ZIP, duplicate entries and runtime Npgsql.
- Ran all three entry points with Python commands blocked and from another working directory; verified
  relative package paths and usage errors.

The job execution results above are from the earlier submodule validation. This tooling change does
not add new database execution results or establish Windows/macOS execution of the new entry points.

## Previous cross-platform CI

[GitHub Actions run 39775979253](https://github.com/Mourad-Azouga/stampeded-dotnet/actions/runs/39775979253)
passed on commit `ac71e577407ad179e6ff0fac638ec58a137cb751` before the submodule restructuring:

- Linux x64, Windows x64 and macOS ARM64: native build, .NET build, formatting and ABI tests.
- Linux with PostgreSQL 18: integration tests, combined-platform packaging and package verification.

The updated workflow builds the local connector against the published engine in each native job.
Its cross-platform execution must be verified again; the previous run does not verify that change.

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
