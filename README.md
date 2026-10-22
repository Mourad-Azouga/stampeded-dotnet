# Stampeded.NET

C# bindings to [Stampeded](https://github.com/seuros/stampeded), a PostgreSQL-backed job processor with state machines and behavior trees.

Jobs are stored as rows in PostgreSQL. State machines define the job lifecycle, while behavior trees
drive queue-policy decisions.

This is an independent, unofficial integration maintained by Mourad Azouga. Its C# API calls the
upstream `libstampeded_ffi` C ABI. The upstream library owns database connections, enqueueing,
claiming, heartbeats and outcome reporting. Your application executes the job handlers in C#.

**Development preview.** Packages have not been published to NuGet. The binding currently targets
.NET 10 and PostgreSQL 18+. Native builds and tests have passed on Linux x64, Windows x64 and
macOS ARM64; PostgreSQL integration tests run on Linux. See the [validation record](docs/validation.md)
for the tested revision and remaining verification boundaries.

## Build from source

Install .NET 10, Bash, Python 3, Rust 1.99.0 through rustup, and your platform's C/C++ build tools.
On Windows, use Git Bash with the Visual Studio C++ build tools installed. Bash, Rust and Python
are build tools; applications consuming a package with their native asset do not need them. The
Bash build script calls the Python source verifier; package verification also uses Python.

```sh
git clone --recurse-submodules https://github.com/Mourad-Azouga/stampeded-dotnet.git
cd stampeded-dotnet
bash scripts/build-native.sh
dotnet build Stampeded.slnx -c Release
```

For an existing checkout, initialize the pinned upstream dependency first:

```sh
git submodule update --init --recursive
```

The build script verifies the pinned, unmodified upstream submodule, uses its Cargo.lock, and stages the
shared library in `runtimes/<rid>/native/`. Source projects copy the asset matching the SDK host RID
into their output directory. Override `NativeRuntimeIdentifier` when needed:

```sh
dotnet build Stampeded.slnx -c Release -p:NativeRuntimeIdentifier=linux-x64
```

For a library built elsewhere, set `STAMPEDED_NATIVE_LIBRARY` to its absolute path before the first
Stampeded call. Otherwise .NET uses its normal native-library resolution, including NuGet runtime
assets. Do not change this setting after loading the library.

```csharp
Console.WriteLine(StampededRuntime.Version); // C ABI version, currently 0.1.0
```

## PostgreSQL setup

For a disposable development database:

```sh
docker compose up -d
```

Compose installs the unchanged upstream schema on first initialization. With an existing **fresh**
database, install it once using PostgreSQL 18 tools:

```sh
psql "$STAMPEDED_DATABASE_URL" -v ON_ERROR_STOP=1 -1 \
  -f native/upstream/crates/stampeded/schema/schema.sql
psql "$STAMPEDED_DATABASE_URL" -v ON_ERROR_STOP=1 \
  -c "SELECT stampeded_create_queue('mail');"
```

The Compose URL is `postgres://stampeded:stampeded@localhost:55432/stampeded`.
Run the upstream daemon against the same database/schema for scheduling, recovery, expiry,
retention and administration. Start it with `--no-worker` when the jobs are handled by your C#
application, so its built-in worker does not consume those jobs. Use a daemon compatible with the
pinned upstream revision; the .NET package does not include it.

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
python scripts/verify-upstream.py
dotnet format Stampeded.slnx --verify-no-changes --no-restore
dotnet pack Stampeded.slnx -c Release --no-build -o artifacts/packages
python scripts/verify-package.py artifacts/packages/Stampeded.NET.0.1.0-preview.1.nupkg
```

A package contains only the native assets staged before packing. A local Linux-only package cannot
run on Windows. CI combines the tested platform assets into one package. No package publishing is
automatic.

Read [the architecture guide](docs/architecture.md), [compatibility boundaries](docs/compatibility.md)
and [validation record](docs/validation.md) before adoption.

## Upstream source and license

`native/upstream/` is a Git submodule pointing to the upstream repository at a fixed commit.
`native/upstream.json` records its repository, commit, core version and C ABI version for package
provenance. The build checks that the gitlink, checked-out commit and provenance agree, and rejects
local changes to upstream sources. Builds use the unchanged workspace and Cargo.lock; the FFI
crate disables the core's default features.

To upgrade, review the upstream changes, update the submodule and `native/upstream.json` together,
and rerun the native, database and package checks. The library, C header and schema must all come
from that same revision. Contributors need the submodule; NuGet consumers receive compiled native
assets and do not need a source checkout.

Our C# code uses the BSD-3-Clause license in `LICENSE`. Upstream code and schema retain Abdelkader
Boudih's BSD-3-Clause license in `native/upstream/LICENSE`, also included in the package.
