using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stampeded;

/// <summary>A snapshot of a persisted job. Times are UTC.</summary>
public sealed record Job
{
    public required Guid Id { get; init; }
    public required string Queue { get; init; }
    [JsonPropertyName("task")]
    public required string TaskName { get; init; }
    [JsonPropertyName("args")]
    public required JsonElement Arguments { get; init; }
    [JsonPropertyName("meta")]
    public required JsonElement Metadata { get; init; }
    public required JobState State { get; init; }
    public required short Attempt { get; init; }
    public required short MaxAttempts { get; init; }
    public required short Priority { get; init; }
    public required DateTimeOffset ScheduledAt { get; init; }
    public required DateTimeOffset InsertedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public Guid? ClaimedBy { get; init; }
    public string? TenantId { get; init; }
    public string? LastError { get; init; }
    public string? FailureKind { get; init; }
    public JsonElement? Checkpoint { get; init; }
    [JsonPropertyName("checkpoint_seq")]
    public int CheckpointSequence { get; init; }
    public int ExpireSeconds { get; init; }
    [JsonPropertyName("backoff_base_ms")]
    public short BackoffBaseMilliseconds { get; init; }
    [JsonPropertyName("backoff_max_ms")]
    public int BackoffMaxMilliseconds { get; init; }
    public bool IsTerminal => State is JobState.Completed or JobState.Discarded or JobState.Cancelled;
}

public enum JobState { Available, Claimed, Executing, Retryable, Completed, Discarded, Cancelled }
