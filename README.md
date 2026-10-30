# Stampeded.NET

C# bindings to [Stampeded](https://github.com/seuros/stampeded), a PostgreSQL-backed job processor with state machines and behavior trees.

Jobs are stored as rows in PostgreSQL. State machines define the job lifecycle, while behavior trees
drive queue-policy decisions.

This is an independent, unofficial integration maintained by Mourad Azouga. Its C# API calls the
upstream `libstampeded_ffi` C ABI. The upstream library owns database connections, enqueueing,
claiming, heartbeats and outcome reporting. Your application executes the job handlers in C#.

**Development preview.** The repository builds NuGet packages locally; no publishing workflow is
configured. This binding targets .NET 10, PostgreSQL 18+, the published `stampeded-ffi` 0.4.1
crate and its `stampeded` 0.4.1 core (schema version 5). On Linux x64, the native build, four
database-free FFI checks, nine .NET tests and package verification passed. Twenty tests needing
PostgreSQL, TLS or Mailpit were skipped. The new build still needs Windows and macOS CI runs,
database migration execution and workload validation. Earlier cross-platform CI results apply to
an older baseline. See the [validation record](docs/validation.md) and the
[historical 0.4.0 upstream review](docs/upstream-0.4-review.md).

## Build from source

Install .NET 10, Bash, Rust 1.99.0 through rustup, and your platform's C/C++ build tools.
On Windows, use Git Bash with the Visual Studio C++ build tools installed. Bash and Rust are build
tools; applications consuming a package with their native asset do not need them. All script entry
points use Bash. Verification runs the C# tool in `tools/Stampeded.Verify` using the .NET SDK; it has
no external package dependencies. Python is not required.

```sh
git clone https://github.com/Mourad-Azouga/stampeded-dotnet.git
cd stampeded-dotnet
bash scripts/build-native.sh
dotnet build Stampeded.slnx -c Release
```

The build compiles the published `stampeded-ffi` crate directly. No Rust source or Git submodule
is kept in this repository. `native/upstream.json` pins its version and archive checksum;
`native/Cargo.lock` pins its dependencies. The shared library is staged in `runtimes/<rid>/native/`.
To refresh the published FFI within its current `0.4.x` series and dependencies:

```sh
bash scripts/build-native.sh --update
```

The update command reads the crates.io index, updates the version and archive checksum pin, and
refreshes the lockfile. Review those changes and rerun the native, database and package checks.
A NuGet release receives an update only after the package is rebuilt and published.

The build prepares the matching crate schema and provenance in `artifacts/native-source/`.
Source projects copy the asset matching the SDK host RID into their output directory.
Override `NativeRuntimeIdentifier` when needed:

```sh
dotnet build Stampeded.slnx -c Release -p:NativeRuntimeIdentifier=linux-x64
```

For a library built elsewhere, set `STAMPEDED_NATIVE_LIBRARY` to its absolute path before the first
Stampeded call. Otherwise .NET uses its normal native-library resolution, including NuGet runtime
assets. Do not change this setting after loading the library.

```csharp
Console.WriteLine(StampededRuntime.Version); // C ABI version, currently 0.4.1
```

## PostgreSQL setup

For a disposable development database:

```sh
bash scripts/prepare-native.sh # also runs as part of build-native.sh
docker compose up -d
```

Compose installs a fresh schema generated from the published upstream migrations on first
initialization. With an existing **fresh** database, install it once using PostgreSQL 18 tools:

```sh
psql "$STAMPEDED_DATABASE_URL" -v ON_ERROR_STOP=1 -1 \
  -f artifacts/native-source/schema.sql
psql "$STAMPEDED_DATABASE_URL" -v ON_ERROR_STOP=1 \
  -c "SELECT stampeded_create_queue('mail');"
```

The Compose URL is `postgres://stampeded:stampeded@localhost:55432/stampeded`.
Run the upstream daemon against the same database/schema for scheduling, recovery, expiry,
retention and administration. Start it with `--no-worker` when the jobs are handled by your C#
application, so its built-in worker does not consume those jobs. Use a daemon compatible with the
resolved core crate version; the .NET package does not include it. Core 0.4.1 records schema version 5.
For an existing database, run that daemon with `--migrate` to upgrade it in place. Connecting from
C# does not migrate the database. The packaged `schema.sql` is for fresh installs only; Compose
initialization scripts also run only when its database volume is first created.

## Enqueue and execute a C# handler

`Stampeded.NET` contains the client, puller, leases and checkpoints.
`Stampeded.NET.Hosting` adds dependency injection, scoped handlers and a hosted worker.

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddStampeded(databaseUrl);
builder.Services.AddStampededHandler<WelcomeEmail, WelcomeEmailHandler>("email.welcome");
builder.Services.AddStampededWorker(options =>
{
    options.Queues = ["mail"];
    options.MaxConcurrency = 4;
});
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(40));

using var host = builder.Build();
var client = host.Services.GetRequiredService<StampededClient>();
var result = client.Enqueue(new EnqueueRequest<WelcomeEmail>
{
    Queue = "mail",
    TaskName = "email.welcome",
    Arguments = new(UserId: 42),
    Options = new() { UniqueKey = "welcome:42", MaxAttempts = 5 }
});
await host.RunAsync();
```

```csharp
public sealed record WelcomeEmail(int UserId);

public sealed class WelcomeEmailHandler(IEmailService email) : IJobHandler<WelcomeEmail>
{
    public Task ExecuteAsync(WelcomeEmail arguments, JobContext context,
        CancellationToken cancellationToken) =>
        email.SendWelcomeAsync(arguments.UserId, context.Job.Id.ToString(), cancellationToken);
}
```

`IEmailService` above represents your application's service. The runnable sample in
`samples/Stampeded.Sample` prepares and logs text; it does not send real email. Run it with
`STAMPEDED_DATABASE_URL` set after provisioning its `mail` queue.

Arguments serialize as JSON objects using snake_case names by default. Supply your own
`JsonSerializerOptions` to `AddStampeded` or `StampededClient.Connect` to use a different contract.
Register handlers for every task in the queues this worker consumes; an unknown task is discarded.

## Direct use without hosting

```csharp
using var client = StampededClient.Connect(databaseUrl);
using var puller = client.StartPuller(new PullerOptions { Queues = ["mail"] });
using var lease = puller.Next(TimeSpan.FromSeconds(5));
if (lease is not null)
{
    // Execute application work, then choose exactly one outcome.
    lease.Complete();
}
```

The C ABI is synchronous and blocks its calling thread. Client and lease methods are synchronous
for that reason. The hosted worker uses a fixed number of dedicated consumer threads to call the
ABI, and supports async C# handlers. It does not expose cancellable native I/O or implement a second
queue engine. Shutdown stops polling, allows a drain period, then requests cooperative handler
cancellation. A handler that ignores cancellation must actually exit before its lease is settled.
Configure the .NET host's shutdown budget to exceed the worker's drain period.

The C ABI owns a separate connection pool. It cannot enqueue or checkpoint inside an existing
Npgsql/EF Core transaction. This binding deliberately exposes no transactional Npgsql overload.

## Retries and progress

- Return normally to complete the job.
- Throw an ordinary exception to fail using upstream backoff and the persisted attempt budget.
- Throw `RetryJobException` for a particular retry delay or `DiscardJobException` for a permanent failure.
- Use `context.Checkpoint(state)` and `context.ResumeFrom<T>()` for resume state.
- Use `context.StepAsync("render", operation, token)` to reuse a persisted step result on retries.
  The upstream library owns the step format, size limits and serialization. Do not nest steps or mix
  raw checkpoints with named steps on the same context.

Jobs may run more than once. A checkpoint cannot atomically cover an external email send or file
write. Derive idempotency keys from the job ID and store references to large files instead of their
contents. The upstream daemon remains necessary for recovery after worker death.

## Verify and package

After building the native library:

```sh
dotnet test --solution Stampeded.slnx -c Release
```

ABI tests always run and require the real native library. PostgreSQL integration tests additionally
need `STAMPEDED_TEST_CONNECTION_STRING` pointing to a disposable PostgreSQL 18 database. The role
must be able to create/drop isolated schemas. Set `STAMPEDED_REQUIRE_DATABASE_TESTS=1` to make a
missing database configuration fail instead of skipping those tests. Npgsql is used only by that
test harness to install the schema and inspect persisted results.

```sh
bash scripts/verify-upstream.sh
dotnet format Stampeded.slnx --verify-no-changes --no-restore
dotnet pack Stampeded.slnx -c Release --no-build -o artifacts/packages
bash scripts/verify-package.sh artifacts/packages/Stampeded.NET.0.1.0-preview.1.nupkg
```

A package contains only the native assets staged before packing. A local Linux-only package cannot
run on Windows. CI combines the tested platform assets into one package. No package publishing is
automatic.

See [the workload testing guide](docs/testing.md) for the local Mailpit inbox, real PDF attachments,
private-CA TLS and required test settings.

Read [the architecture guide](docs/architecture.md), [compatibility boundaries](docs/compatibility.md)
and [validation record](docs/validation.md) before adoption.

## Upstream source and license

The native library comes from the published `stampeded-ffi` crate. Its C header and the matching
core migrations are copied from the resolved crates during preparation. The FFI archive checksum
is pinned in `native/upstream.json`; the core version and checksum are pinned in `native/Cargo.lock`.
The published FFI selects AWS-LC TLS by default.

`scripts/prepare-native.sh` extracts the migrations from the resolved core crate, generates a fresh
install SQL script with the SQLx migration ledger, and records the exact crate versions and checksums.
Packaging includes that provenance, the lockfile, published header, migrations, fresh-install script
and upstream license.
Verification rejects stale prepared files after native inputs change.

To upgrade within `0.4.x`, run `bash scripts/build-native.sh --update`, review and commit the pins,
and rerun the native, database and package checks. A new release series requires an explicit pin change.
NuGet consumers receive compiled native assets and do not need Rust or a source checkout.

Our C# code uses the BSD-3-Clause license in `LICENSE`. Upstream code and schema retain Abdelkader
Boudih's BSD-3-Clause license in `native/LICENSE`, also included in the package.
