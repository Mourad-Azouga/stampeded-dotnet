# Architecture and reading guide

The .NET API binds to the existing upstream Rust C ABI. It does not contain its own SQL job store,
claim loop, heartbeat loop, retry calculations, scheduler or maintenance daemon.

```text
Application service / ASP.NET / console application
                    |
Stampeded.Hosting: DI scopes, typed dispatch, cooperative cancellation
                    |
Stampeded: requests, snapshots, client, puller, lease, context
                    |
Interop: LibraryImport, UTF-8, C structs, SafeHandle, status/error conversion
                    |
Local libstampeded_ffi connector -> published stampeded crate -> PostgreSQL
```

The scheduler and maintenance daemon run separately using the upstream project's binaries.

## Read the implementation in this order

1. `src/Stampeded/Interop/NativeMethods.cs`: compare signatures with the upstream `stampeded.h`.
2. `NativeHandles.cs`, `NativeOptions.cs`, `NativeCall.cs`: follow ownership, byte layout and errors.
3. `StampededClient.cs`: follow connect, typed argument serialization and native enqueue.
4. `StampededPuller.cs`, `JobLease.cs`: follow polling, snapshot copying and consuming outcomes.
5. `JobSteps.cs`, `JobContext.cs`: Rust step records and persisted checkpoints.
6. `IJobHandler.cs` and `samples/Stampeded.Sample/Program.cs`: application-level handler contract.
7. `src/Stampeded.Hosting`: scoped dispatch and host lifecycle.
8. `tests/Stampeded.Tests`: ABI boundary, database results and hosting behavior.

## Native lifetime rules

The C ABI exposes opaque pointers, never Rust structs. A client owns a Rust runtime and connection
pool. Pullers and leases retain their upstream shared runtime; they can outlive the managed client.

`SafeHandle` protects each client/puller P/Invoke against concurrent disposal. Lease and step
operations additionally serialize access because their native objects expose borrowed strings and
consuming mutations. All returned strings are copied before releasing the relevant lock or making
another native call that could invalidate them.

A lease outcome consumes and frees the native lease **even when reporting fails**. `JobLease`
invalidates its handle in a finally block after the native outcome call, preventing disposal or
another outcome from freeing the same pointer. Without an outcome, disposing a lease calls the
upstream interruption path. Borrowed job JSON is copied into a managed snapshot before consumption.

Constructors must dispose newly created handles when managed initialization fails. The handle
releasers are private implementation details; application code never manipulates pointers.

## Error handling

The C ABI's last error is thread-local and is invalidated by another guarded native call. Each
wrapper reads it immediately, before any await or native cleanup, and copies it into a
`StampededException`. `STAMPEDED_NONE` is an ordinary result whose meaning depends on the function:
for example, a duplicate enqueue or a poll without work. A checkpoint returning NONE means lost
ownership and raises `JobOwnershipLostException`.

An outcome report may fail after PostgreSQL committed. The hosting layer logs that uncertainty and
does not run the handler again itself. Recovery and persisted state determine what happens next.

## Blocking calls and host behavior

The Rust C ABI blocks its calling thread and does not call back into managed code. The client API
therefore exposes synchronous operations. An Async suffix would misrepresent their I/O behavior.
`StepAsync` is asynchronous because it awaits the application's step operation; persisting its
result still makes a blocking native call.

The hosted worker starts a fixed number of dedicated consumer threads. Each waits for one lease,
awaits the asynchronous handler through its task, and reports one outcome before fetching another.
The async handler's continuations can use the .NET ThreadPool; native polling does not occupy an
unbounded number of ThreadPool threads. Rust controls queue policies, batching, wakeups and worker
heartbeats through its Puller implementation.

Each job attempt gets a fresh asynchronous DI scope. A missing handler or an invalid typed payload
is discarded. Application exceptions use upstream retries, with explicit retry/discard exceptions
for application decisions. The wrapper does not implement a second retry loop.

## Shutdown and deadlines

Shutdown stops fetching more jobs. Active handlers get the configured drain period before their
cancellation token is cancelled. A separate timer enforces the configured cooperative job timeout.
When the handler actually exits, cancellation caused by shutdown requeues its lease, while a job
timeout reports `timed_out`. A completed handler that exceeded its timer also reports timeout.

An uncooperative handler cannot be forcibly terminated safely. Its lease remains active until the
handler exits or the process ends; host cancellation is not proof that its external effects stopped.
Native database operations themselves have no CancellationToken. A poll timeout is a work-wait
budget, not a guarantee that a slow database call returns within that time.

The .NET host's own shutdown budget must leave time for draining and native outcome reporting.
Run the upstream daemon for expiry and recovery after process death.

## Checkpoints and steps

Named-step state is loaded and updated through Rust's `stampeded_steps_*` functions, including its
format and size rules. C# serializes the application-specific values. After a successful operation,
the wrapper checkpoints Rust's resulting record through the lease; it adopts that record locally
only after the checkpoint succeeds. A retry can read completed results from the persisted record.

Keep one coordinated progress flow per context. Do not nest StepAsync calls or mix raw checkpoint
writes with named steps. The snapshot's checkpoint reflects the state at claim; ResumeFrom reads
the context's most recent accepted checkpoint.

Neither a Rust checkpoint nor an enqueue joins a caller-owned Npgsql/EF Core transaction. There are
no such overloads in this binding. External sends and file writes still need idempotency because a
process can fail after an effect and before persisting its checkpoint.

## Packaging and source provenance

`native/stampeded-ffi/` retains the upstream C ABI source and header locally. Its origin commit is
recorded in `native/upstream.json`. The connector depends on the published `stampeded` crate with
default features disabled and AWS-LC TLS selected. `native/Cargo.lock` records the exact engine and
transitive dependencies. There is no Git submodule or local copy of the engine.

Preparation reads Cargo's resolved package metadata, requires the engine to come from crates.io,
and copies its schema into `artifacts/native-source/`. Generated package provenance records the
core version/checksum, connector origin and native input hashes; verification rejects stale inputs
or a modified prepared schema. The lockfile, header, schema and upstream license accompany the package.
Native assets use standard `runtimes/<rid>/native/` NuGet paths. Built assets, Cargo outputs and .NET
artifacts are ignored by Git.

`build-native.sh --update` refreshes compatible core releases; normal builds use `--locked`.
Dependabot checks Cargo dependencies weekly. Incompatible core releases and changes to the local
connector require review and validation before releasing rebuilt NuGet packages.

Interop practices follow Microsoft's [native interoperability guidance](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices):
LibraryImport, explicit ABI types and UTF-8 strings, and SafeHandle resource ownership.

## Build verification

All developer script entry points are Bash scripts in `scripts/`. The native build prepares the
resolved crate's schema and provenance before compiling the connector. The verifier entry points run `tools/Stampeded.Verify`,
a non-packable C# console tool included in the solution's build and formatting checks. It uses
.NET's JSON, ZIP and XML APIs to validate source provenance and package contents. No Python or
third-party parsing tools are required. The tool is not a runtime dependency of either NuGet package.
