using System.Text.Json;

namespace Stampeded;

/// <summary>Checkpoint operations on the Rust-owned lease for this attempt.</summary>
public sealed class JobContext
{
    private readonly JobLease _lease;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _stepGate = new(1, 1);
    private string? _checkpoint;
    public Job Job => _lease.Job;
    public Guid WorkerId { get; }

    internal JobContext(JobLease lease, Guid workerId, JsonSerializerOptions jsonOptions)
    {
        _lease = lease;
        WorkerId = workerId;
        _jsonOptions = jsonOptions;
        _checkpoint = Job.Checkpoint?.GetRawText();
    }

    public T? ResumeFrom<T>() => _checkpoint is { } json ? JsonSerializer.Deserialize<T>(json, _jsonOptions) : default;

    /// <summary>Synchronously persist resume state through Rust. Cannot join a caller's .NET transaction.</summary>
    public void Checkpoint<T>(T state)
    {
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        _lease.Checkpoint(json);
        _checkpoint = json;
    }

    /// <summary>
    /// Reuse a completed step's result using Rust's step format. Effects still require idempotency.
    /// Steps execute sequentially in this context. Do not nest steps or mix them with raw checkpoints.
    /// </summary>
    public async Task<T> StepAsync<T>(string name, Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(operation);
        await _stepGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lease.EnsureActive();
            using var steps = JobSteps.Load(_checkpoint);
            if (steps.IsCompleted(name))
                return steps.GetValue(name) is { } value ? value.Deserialize<T>(_jsonOptions)! : default!;

            var result = await operation(cancellationToken).ConfigureAwait(false);
            steps.Complete(name, JsonSerializer.SerializeToElement(result, _jsonOptions));
            var json = steps.ToJson();
            _lease.Checkpoint(json);
            _checkpoint = json;
            return result;
        }
        finally
        {
            _stepGate.Release();
        }
    }
}
