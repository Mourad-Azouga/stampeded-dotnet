using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stampeded.Hosting;

namespace Stampeded.Tests;

public sealed class HostingTests
{
    private static IHost CreateHost(Database database, Action<StampededWorkerOptions>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddStampeded(database.Url);
        builder.Services.AddSingleton<Tracking>();
        builder.Services.AddStampededHandler<Message, MessageHandler>("test.handler");
        builder.Services.AddStampededWorker(options =>
        {
            options.Queues = ["mail"];
            options.MaxConcurrency = 2;
            options.BatchSize = 2;
            options.PollInterval = TimeSpan.FromMilliseconds(20);
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
            options.ShutdownTimeout = TimeSpan.FromMilliseconds(100);
            configure?.Invoke(options);
        });
        return builder.Build();
    }

    private static Guid Enqueue(Database database, string mode = "success", string task = "test.handler") =>
        database.Client.Enqueue(new EnqueueRequest<Message>
        {
            Queue = "mail",
            TaskName = task,
            Arguments = new(mode),
            Options = new() { MaxAttempts = 2 }
        }).JobId!.Value;

    [PostgresFact]
    public async Task ResolvesScopedCSharpHandlersAndCompletesJobsThroughRust()
    {
        await using var database = await Database.CreateAsync();
        using var host = CreateHost(database);
        var first = Enqueue(database);
        var second = Enqueue(database);
        await host.StartAsync();
        try
        {
            await database.WaitForAsync(first, "completed");
            await database.WaitForAsync(second, "completed");
        }
        finally { await host.StopAsync(); }
        var tracking = host.Services.GetRequiredService<Tracking>();
        Assert.Equal(2, tracking.Instances.Count);
        Assert.Equal(2, tracking.Disposed);
    }

    [PostgresFact]
    public async Task RetriesHandlerFailuresAndDiscardsUnknownTasks()
    {
        await using var database = await Database.CreateAsync();
        using var host = CreateHost(database);
        var retried = Enqueue(database, "retry");
        var unknown = Enqueue(database, task: "missing.handler");
        await host.StartAsync();
        try
        {
            var row = await database.WaitForAsync(retried, "completed");
            Assert.Equal(2, row.GetProperty("attempt").GetInt32());
            await database.WaitForAsync(unknown, "discarded");
        }
        finally { await host.StopAsync(); }
    }

    [PostgresFact]
    public async Task ReportsCooperativeTimeoutsToRust()
    {
        await using var database = await Database.CreateAsync();
        using var host = CreateHost(database, options => options.JobTimeout = TimeSpan.FromMilliseconds(50));
        var id = Enqueue(database, "wait");
        // Avoid waiting for the default backoff by making the first timeout terminal.
        await database.ExecuteAsync("UPDATE stampeded_jobs SET max_attempts = 1");
        await host.StartAsync();
        try
        {
            var row = await database.WaitForAsync(id, "discarded");
            Assert.Equal("timed_out", row.GetProperty("failure_kind").GetString());
        }
        finally { await host.StopAsync(); }
    }

    [PostgresFact]
    public async Task ShutdownCancelsAHandlerAfterTheDrainPeriodAndRequeuesItsLease()
    {
        await using var database = await Database.CreateAsync();
        using var host = CreateHost(database);
        var id = Enqueue(database, "wait");
        await host.StartAsync();
        await host.Services.GetRequiredService<Tracking>().Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.StopAsync();
        var row = await database.WaitForAsync(id, "available");
        Assert.Equal(0, row.GetProperty("attempt").GetInt32());
    }

    [Fact]
    public void DuplicateClientRegistrationIsRejected()
    {
        var services = new ServiceCollection();
        services.AddStampeded("postgres://localhost/test");
        Assert.Throws<InvalidOperationException>(() => services.AddStampeded("postgres://localhost/test"));
    }

    public sealed record Message(string Mode);

    public sealed class Tracking
    {
        public System.Collections.Concurrent.ConcurrentBag<Guid> Instances { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposed;
    }

    public sealed class MessageHandler(Tracking tracking) : IJobHandler<Message>, IDisposable
    {
        private readonly Guid _id = Guid.NewGuid();

        public async Task ExecuteAsync(Message arguments, JobContext context, CancellationToken cancellationToken)
        {
            tracking.Instances.Add(_id);
            tracking.Started.TrySetResult();
            if (arguments.Mode == "retry" && context.Job.Attempt == 1)
                throw new RetryJobException(TimeSpan.Zero, "try again");
            if (arguments.Mode == "wait")
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            await context.StepAsync("prepare", _ => Task.FromResult("ready"), cancellationToken);
        }

        public void Dispose() => Interlocked.Increment(ref tracking.Disposed);
    }
}
