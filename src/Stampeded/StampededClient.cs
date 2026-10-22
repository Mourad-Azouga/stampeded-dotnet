using System.Text.Json;
using Stampeded.Interop;

namespace Stampeded;

/// <summary>Owns a Rust connection pool and runtime. Native operations block the calling thread.</summary>
public sealed class StampededClient : IDisposable
{
    private readonly ClientHandle _handle;
    internal JsonSerializerOptions JsonOptions { get; }

    private StampededClient(ClientHandle handle, JsonSerializerOptions? serializerOptions)
    {
        _handle = handle;
        JsonOptions = serializerOptions is null ? JobJson.DefaultOptions : new(serializerOptions);
        JsonOptions.MakeReadOnly(populateMissingResolver: true);
    }

    /// <summary>Connect using a PostgreSQL URL; the upstream schema must already be installed.</summary>
    public static StampededClient Connect(string databaseUrl, JsonSerializerOptions? serializerOptions = null)
    {
        NativeCall.ValidateString(databaseUrl, nameof(databaseUrl));
        var pointer = NativeMethods.Connect(databaseUrl);
        if (pointer == 0)
            throw NativeCall.Error("connect");
        var handle = new ClientHandle(pointer);
        try
        {
            return new(handle, serializerOptions);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public EnqueueResult Enqueue<TArguments>(EnqueueRequest<TArguments> request)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        ArgumentNullException.ThrowIfNull(request);
        NativeCall.ValidateString(request.Queue, nameof(request.Queue));
        NativeCall.ValidateString(request.TaskName, nameof(request.TaskName));
        ArgumentNullException.ThrowIfNull(request.Options);
        request.Options.Validate();
        var arguments = JobJson.SerializeObject(request.Arguments, JsonOptions);
        using var metadata = new Utf8String(request.Options.Metadata?.GetRawText());
        using var unique = new Utf8String(request.Options.UniqueKey);
        var options = new NativeEnqueueOptions
        {
            Metadata = metadata.Pointer,
            UniqueKey = unique.Pointer,
            Priority = request.Options.Priority,
            MaxAttempts = request.Options.MaxAttempts,
            ScheduledAtUnixMilliseconds = request.Options.ScheduledAt?.ToUnixTimeMilliseconds() ?? 0
        };
        return NativeCall.Succeeded(NativeMethods.Enqueue(_handle, request.Queue, request.TaskName,
            arguments, in options, out var id), "enqueue") ? new(id.ToGuid()) : new(null);
    }

    public StampededPuller StartPuller(PullerOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        options ??= new();
        options.Validate();
        using var queues = new Utf8String(string.Join(',', options.Queues));
        using var except = new Utf8String(options.ExceptQueues.Length == 0 ? null : string.Join(',', options.ExceptQueues));
        var native = new NativePullerOptions
        {
            Queues = queues.Pointer,
            ExceptQueues = except.Pointer,
            MaxLeases = checked((uint)options.MaxLeases),
            BatchSize = checked((uint)options.BatchSize),
            PollIntervalMilliseconds = NativeCall.Milliseconds(options.PollInterval, nameof(options.PollInterval)),
            HeartbeatIntervalMilliseconds = NativeCall.Milliseconds(options.HeartbeatInterval, nameof(options.HeartbeatInterval))
        };
        var pointer = NativeMethods.PullerStart(_handle, in native);
        if (pointer == 0)
            throw NativeCall.Error("puller_start");
        var handle = new PullerHandle(pointer);
        try
        {
            return new(handle, JsonOptions);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Release this client. Existing pullers retain their upstream runtime and pool.</summary>
    public void Dispose() => _handle.Dispose();
}
