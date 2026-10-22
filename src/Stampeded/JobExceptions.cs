namespace Stampeded;

/// <summary>Choose a retry delay without bypassing the job's attempt budget.</summary>
public sealed class RetryJobException : Exception
{
    public TimeSpan RetryAfter { get; }

    public RetryJobException(TimeSpan retryAfter, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryAfter, TimeSpan.Zero);
        RetryAfter = retryAfter;
    }
}

/// <summary>A permanent handler failure that should immediately discard the job.</summary>
public sealed class DiscardJobException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The database no longer accepts writes from this worker and attempt.</summary>
public sealed class JobOwnershipLostException(Guid jobId)
    : InvalidOperationException($"Ownership of job {jobId} was lost. Stop processing this attempt.")
{
    public Guid JobId { get; } = jobId;
}
