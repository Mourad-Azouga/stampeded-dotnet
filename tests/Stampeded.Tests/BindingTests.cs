using System.Text.Json;

namespace Stampeded.Tests;

public sealed class BindingTests
{
    private static EnqueueRequest<object> Request(object? arguments = null) => new()
    {
        Queue = "mail",
        TaskName = "email.welcome",
        Arguments = arguments ?? new { user_id = 42, subject = "مرحبا résumé" }
    };

    private static StampededPuller Puller(StampededClient client, int concurrency = 1) => client.StartPuller(new()
    {
        Queues = ["mail"],
        MaxLeases = concurrency,
        BatchSize = concurrency,
        PollInterval = TimeSpan.FromMilliseconds(20),
        HeartbeatInterval = TimeSpan.FromMilliseconds(100)
    });

    [PostgresFact]
    public async Task EnqueuesPullsAndCompletesAUnicodePayloadThroughRust()
    {
        await using var database = await Database.CreateAsync();
        var result = database.Client.Enqueue(Request());
        using var puller = Puller(database.Client);
        using var lease = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3)));
        Assert.Equal(result.JobId, lease.Job.Id);
        Assert.Equal("email.welcome", lease.Job.TaskName);
        Assert.Equal("مرحبا résumé", lease.Job.Arguments.GetProperty("subject").GetString());
        Assert.Equal(1, lease.Job.Attempt);
        lease.Complete();
        Assert.Throws<ObjectDisposedException>(lease.Complete);
        var lateStepRan = false;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lease.Context.StepAsync("late", _ =>
        {
            lateStepRan = true;
            return Task.FromResult("must not run");
        }));
        Assert.False(lateStepRan);
        var row = await database.WaitForAsync(result.JobId!.Value, "completed");
        Assert.Equal(1, row.GetProperty("attempt").GetInt32());
        // Snapshots survive the consumed native pointer.
        Assert.Equal(42, lease.Job.Arguments.GetProperty("user_id").GetInt32());
    }

    [PostgresFact]
    public async Task DuplicateScopeReturnsNoSecondId()
    {
        await using var database = await Database.CreateAsync();
        var request = Request() with { Options = new() { UniqueKey = "welcome:42" } };
        Assert.True(database.Client.Enqueue(request).Enqueued);
        Assert.False(database.Client.Enqueue(request).Enqueued);
    }

    [PostgresFact]
    public async Task CheckpointsSurviveRetryAndRustEnforcesTheAttemptBudget()
    {
        await using var database = await Database.CreateAsync();
        var id = database.Client.Enqueue(Request() with { Options = new() { MaxAttempts = 2 } }).JobId!.Value;
        using var puller = Puller(database.Client);
        using (var first = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3))))
        {
            first.Context.Checkpoint(new { page = 7 });
            first.Fail("temporary failure", TimeSpan.Zero);
            Assert.Throws<ObjectDisposedException>(() => first.Fail("again"));
        }
        using (var second = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3))))
        {
            Assert.Equal(2, second.Job.Attempt);
            Assert.Equal(7, second.Context.ResumeFrom<JsonElement>().GetProperty("page").GetInt32());
            second.Fail("permanent failure", TimeSpan.Zero);
        }
        var row = await database.WaitForAsync(id, "discarded");
        Assert.Equal(2, row.GetProperty("attempt").GetInt32());
    }

    [PostgresFact]
    public async Task StepsReuseThePersistedResultOnTheNextAttempt()
    {
        await using var database = await Database.CreateAsync();
        database.Client.Enqueue(Request());
        using var puller = Puller(database.Client);
        var calls = 0;
        using (var first = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3))))
        {
            Assert.Equal("file.pdf", await first.Context.StepAsync("render", _ => { calls++; return Task.FromResult("file.pdf"); }));
            first.Fail("send later", TimeSpan.Zero);
        }
        using var second = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3)));
        Assert.Equal("file.pdf", await second.Context.StepAsync("render", _ => { calls++; return Task.FromResult("wrong"); }));
        Assert.Equal(1, calls);
        second.Complete();
    }

    [PostgresFact]
    public async Task StaleCheckpointReportsOwnershipLoss()
    {
        await using var database = await Database.CreateAsync();
        database.Client.Enqueue(Request());
        using var puller = Puller(database.Client);
        using var lease = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3)));
        await database.ExecuteAsync("UPDATE stampeded_jobs SET attempt = attempt + 1 WHERE state = 'executing'");
        Assert.Throws<JobOwnershipLostException>(() => lease.Context.Checkpoint(new { page = 1 }));
    }

    [PostgresFact]
    public async Task RequeueRefundsTheAttemptAndDiscardTerminatesIt()
    {
        await using var database = await Database.CreateAsync();
        var id = database.Client.Enqueue(Request()).JobId!.Value;
        using var puller = Puller(database.Client);
        using (var first = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3))))
            first.Requeue();
        using var second = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, second.Job.Attempt);
        second.Discard("invalid address");
        var row = await database.WaitForAsync(id, "discarded");
        Assert.Equal("invalid address", row.GetProperty("last_error").GetString());
    }

    [PostgresFact]
    public async Task DisposingAnUnsettledLeaseReportsAnInterruption()
    {
        await using var database = await Database.CreateAsync();
        var id = database.Client.Enqueue(Request()).JobId!.Value;
        using var puller = Puller(database.Client);
        Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3))).Dispose();
        var row = await database.WaitForAsync(id, "retryable");
        Assert.Equal(1, row.GetProperty("interruptions").GetInt32());
    }

    [PostgresFact]
    public async Task PullerOutlivesItsClientAndStopPreventsFurtherPulls()
    {
        await using var database = await Database.CreateAsync();
        var client = StampededClient.Connect(database.Url);
        using var puller = Puller(client);
        database.Client.Enqueue(Request());
        client.Dispose();
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.Enqueue(Request()));
        using var lease = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3)));
        lease.Complete();
        puller.Stop(TimeSpan.Zero);
        Assert.Null(puller.Next(TimeSpan.FromMilliseconds(20)));
        puller.Dispose();
        Assert.Throws<ObjectDisposedException>(() => puller.Next(TimeSpan.Zero));
    }

    [PostgresFact]
    public async Task NativeErrorDoesNotLeaveTheLeaseAvailableForDoubleFree()
    {
        await using var database = await Database.CreateAsync();
        database.Client.Enqueue(Request());
        using var puller = Puller(database.Client);
        using var lease = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3)));
        await database.ExecuteAsync("CREATE FUNCTION reject_completion() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'binding test failure'; END $$; CREATE TRIGGER reject_completion BEFORE UPDATE ON stampeded_jobs FOR EACH ROW EXECUTE FUNCTION reject_completion()");
        Assert.Throws<StampededException>(lease.Complete);
        Assert.Throws<ObjectDisposedException>(lease.Complete);
        lease.Dispose();
    }

    [PostgresFact]
    public async Task ConcurrentOutcomesCannotConsumeTheSameLeaseTwice()
    {
        await using var database = await Database.CreateAsync();
        database.Client.Enqueue(Request());
        using var puller = Puller(database.Client);
        using var lease = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3)));
        var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            try { lease.Complete(); return true; }
            catch (ObjectDisposedException) { return false; }
        }));
        Assert.Single(await Task.WhenAll(attempts), success => success);
    }

    [PostgresFact]
    public async Task MetadataPriorityAndSchedulingReachTheUpstreamQueue()
    {
        await using var database = await Database.CreateAsync();
        var at = DateTimeOffset.UtcNow.AddHours(1);
        var id = database.Client.Enqueue(Request() with
        {
            Options = new() { Priority = 9, MaxAttempts = 3, ScheduledAt = at, Metadata = JsonSerializer.SerializeToElement(new { source = "dotnet" }) }
        }).JobId!.Value;
        var row = await database.ReadAsync(id);
        Assert.Equal(9, row.GetProperty("priority").GetInt32());
        Assert.Equal(3, row.GetProperty("max_attempts").GetInt32());
        Assert.Equal("dotnet", row.GetProperty("meta").GetProperty("source").GetString());
        Assert.True((row.GetProperty("scheduled_at").GetDateTimeOffset() - at).Duration() < TimeSpan.FromMilliseconds(2));
        using var puller = Puller(database.Client);
        Assert.Null(puller.Next(TimeSpan.FromMilliseconds(50)));
    }
}
