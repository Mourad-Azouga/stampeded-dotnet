using Stampeded.Interop;

namespace Stampeded;

public sealed record PullerOptions
{
    public string[] Queues { get; init; } = ["default"];
    public string[] ExceptQueues { get; init; } = [];
    public int MaxLeases { get; init; } = 10;
    public int BatchSize { get; init; } = 5;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Queues);
        ArgumentNullException.ThrowIfNull(ExceptQueues);
        if (Queues.Length == 0)
            throw new ArgumentException("Specify at least one queue.", nameof(Queues));
        foreach (var queue in Queues.Concat(ExceptQueues))
        {
            NativeCall.ValidateString(queue, nameof(Queues));
            if (queue.Contains(',') || queue != queue.Trim())
                throw new ArgumentException("Specify individual queue names without commas or surrounding whitespace.", nameof(Queues));
        }
        if (Queues.Contains("*", StringComparer.Ordinal) && Queues.Length != 1)
            throw new ArgumentException("The wildcard must be the only queue selection.", nameof(Queues));
        if (ExceptQueues.Contains("*", StringComparer.Ordinal) || (ExceptQueues.Length > 0 && !Queues.Contains("*", StringComparer.Ordinal)))
            throw new ArgumentException("Exclusions require wildcard selection and explicit queue names.", nameof(ExceptQueues));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxLeases, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize, 1);
        NativeCall.Milliseconds(PollInterval, nameof(PollInterval));
        NativeCall.Milliseconds(HeartbeatInterval, nameof(HeartbeatInterval));
    }
}
