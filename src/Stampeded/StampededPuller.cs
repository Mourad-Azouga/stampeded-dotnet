using System.Text.Json;
using Stampeded.Interop;

namespace Stampeded;

/// <summary>Rust owns claiming, wakeups and heartbeats. Call Next concurrently to run multiple jobs.</summary>
public sealed class StampededPuller : IDisposable
{
    private readonly PullerHandle _handle;
    private readonly JsonSerializerOptions _jsonOptions;
    private int _disposed;
    public Guid WorkerId { get; }

    internal StampededPuller(PullerHandle handle, JsonSerializerOptions jsonOptions)
    {
        _handle = handle;
        _jsonOptions = jsonOptions;
        WorkerId = Guid.Parse(NativeCall.String(NativeMethods.PullerWorkerId(handle))!);
    }

    /// <summary>Block up to the timeout. Returns null when no lease is available or pulling has stopped.</summary>
    public JobLease? Next(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var milliseconds = NativeCall.Milliseconds(timeout, nameof(timeout), allowZero: true);
        var status = NativeMethods.PullerNext(_handle, milliseconds, out var pointer);
        if (!NativeCall.Succeeded(status, "puller_next"))
            return null;
        var handle = new LeaseHandle(pointer);
        try
        {
            return new(handle, WorkerId, _jsonOptions);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Stop pulling, return prefetched jobs, and wait up to the drain timeout for outstanding leases.</summary>
    public void Stop(TimeSpan drainTimeout)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        NativeCall.Succeeded(NativeMethods.PullerStop(_handle,
            NativeCall.Milliseconds(drainTimeout, nameof(drainTimeout), allowZero: true)), "puller_stop");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            NativeCall.Succeeded(NativeMethods.PullerStop(_handle, 0), "puller_stop");
        }
        finally
        {
            _handle.Dispose();
        }
    }
}
