namespace Stampeded.Hosting;

public sealed class StampededWorkerOptions
{
    public string[] Queues { get; set; } = ["default"];
    public string[] ExceptQueues { get; set; } = [];
    public int MaxConcurrency { get; set; } = 10;
    public int BatchSize { get; set; } = 5;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(25);

    internal PullerOptions ToPullerOptions() => new()
    {
        Queues = Queues.ToArray(),
        ExceptQueues = ExceptQueues.ToArray(),
        MaxLeases = MaxConcurrency,
        BatchSize = BatchSize,
        PollInterval = PollInterval,
        HeartbeatInterval = HeartbeatInterval
    };

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Queues);
        ArgumentNullException.ThrowIfNull(ExceptQueues);
        ToPullerOptions().Validate();
        ValidateTimeout(JobTimeout, nameof(JobTimeout));
        ValidateTimeout(ShutdownTimeout, nameof(ShutdownTimeout));
    }

    private static void ValidateTimeout(TimeSpan duration, string name)
    {
        if (duration <= TimeSpan.Zero || duration.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(name, "Use a positive timeout of less than 49 days.");
    }
}
