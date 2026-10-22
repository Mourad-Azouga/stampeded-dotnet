using System.Text.Json;
using Stampeded.Interop;

namespace Stampeded;

/// <summary>One job attempt. Exactly one outcome or disposal consumes its native lease.</summary>
public sealed class JobLease : IDisposable
{
    private readonly object _gate = new();
    private readonly LeaseHandle _handle;
    public Job Job { get; }
    public JobContext Context { get; }

    internal JobLease(LeaseHandle handle, Guid workerId, JsonSerializerOptions jsonOptions)
    {
        _handle = handle;
        // Copy borrowed native strings before any operation can consume this lease.
        Job = JobJson.ParseJob(NativeCall.String(NativeMethods.LeaseJob(handle))!);
        Context = new(this, workerId, jsonOptions);
    }

    public void Complete() => Settle(NativeMethods.LeaseComplete, "lease_complete");

    /// <summary>Use Rust's backoff when retryAfter is null. The upstream attempt budget still applies.</summary>
    public void Fail(string message, TimeSpan? retryAfter = null)
    {
        NativeCall.ValidateString(message, nameof(message));
        if (retryAfter < TimeSpan.Zero || retryAfter?.TotalMilliseconds > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(retryAfter));
        var milliseconds = retryAfter is { } delay ? checked((long)Math.Ceiling(delay.TotalMilliseconds)) : -1;
        Settle(pointer => NativeMethods.LeaseFail(pointer, message, milliseconds), "lease_fail");
    }

    public void Requeue() => Settle(NativeMethods.LeaseRequeue, "lease_requeue");

    public void Discard(string message)
    {
        NativeCall.ValidateString(message, nameof(message));
        Settle(pointer => NativeMethods.LeaseDiscard(pointer, message), "lease_discard");
    }

    public void TimeOut(string message)
    {
        NativeCall.ValidateString(message, nameof(message));
        Settle(pointer => NativeMethods.LeaseTimedOut(pointer, message), "lease_timed_out");
    }

    internal void Checkpoint(string json)
    {
        lock (_gate)
        {
            ThrowIfConsumed();
            if (!NativeCall.Succeeded(NativeMethods.LeaseCheckpoint(_handle, json), "lease_checkpoint"))
                throw new JobOwnershipLostException(Job.Id);
        }
    }

    private void Settle(Func<nint, int> operation, string name)
    {
        lock (_gate)
        {
            ThrowIfConsumed();
            try
            {
                // The C ABI consumes the pointer even when the operation reports an error.
                NativeCall.Succeeded(operation(_handle.DangerousGetHandle()), name);
            }
            finally
            {
                _handle.SetHandleAsInvalid();
                _handle.Dispose();
            }
        }
    }

    private void ThrowIfConsumed() => ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

    internal void EnsureActive()
    {
        lock (_gate)
            ThrowIfConsumed();
    }

    /// <summary>Without an outcome, Rust returns the attempt as an interruption.</summary>
    public void Dispose()
    {
        lock (_gate)
            _handle.Dispose();
    }
}
