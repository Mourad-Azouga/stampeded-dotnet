using System.Text.Json;
using Stampeded.Interop;

namespace Stampeded;

/// <summary>A task name and named arguments, portable across Stampeded runtimes.</summary>
public sealed record EnqueueRequest<TArguments>
{
    public required string Queue { get; init; }
    public required string TaskName { get; init; }
    public required TArguments Arguments { get; init; }
    public EnqueueOptions Options { get; init; } = new();
}

/// <summary>Only settings exposed by the upstream enqueue C ABI.</summary>
public sealed record EnqueueOptions
{
    public short Priority { get; init; }
    public short MaxAttempts { get; init; } = 25;
    public DateTimeOffset? ScheduledAt { get; init; }
    public string? UniqueKey { get; init; }
    public JsonElement? Metadata { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxAttempts, 1);
        if (UniqueKey is not null)
            NativeCall.ValidateString(UniqueKey, nameof(UniqueKey));
        if (Metadata is { ValueKind: not JsonValueKind.Object })
            throw new ArgumentException("Metadata must be a JSON object.", nameof(Metadata));
    }
}

/// <summary>An existing unique scope returns no ID instead of creating a second job.</summary>
public readonly record struct EnqueueResult(Guid? JobId)
{
    public bool Enqueued => JobId.HasValue;
}
