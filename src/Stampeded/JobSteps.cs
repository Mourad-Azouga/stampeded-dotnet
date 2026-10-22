using System.Text.Json;
using Stampeded.Interop;

namespace Stampeded;

/// <summary>The upstream Rust step-record format and validation. This object owns no database connection.</summary>
public sealed class JobSteps : IDisposable
{
    private readonly object _gate = new();
    private readonly StepsHandle _handle;

    private JobSteps(StepsHandle handle) => _handle = handle;

    public static JobSteps Load(string? checkpointJson = null)
    {
        if (checkpointJson is not null)
            NativeCall.ValidateString(checkpointJson, nameof(checkpointJson));
        var pointer = NativeMethods.StepsLoad(checkpointJson);
        if (pointer == 0)
            throw NativeCall.Error("steps_load");
        return new(new StepsHandle(pointer));
    }

    public bool IsCompleted(string name)
    {
        NativeCall.ValidateString(name, nameof(name));
        lock (_gate)
        {
            ThrowIfDisposed();
            return NativeCall.Succeeded(NativeMethods.StepsCompleted(_handle, name), "steps_completed");
        }
    }

    public JsonElement? GetValue(string name) => Read(name, NativeMethods.StepsValue);
    public JsonElement? GetCursor(string name) => Read(name, NativeMethods.StepsCursor);

    private JsonElement? Read(string name, Func<StepsHandle, string, nint> operation)
    {
        NativeCall.ValidateString(name, nameof(name));
        lock (_gate)
        {
            ThrowIfDisposed();
            var json = NativeCall.String(operation(_handle, name));
            return json is null ? null : JobJson.Parse(json);
        }
    }

    public void Complete(string name, JsonElement? value = null)
    {
        NativeCall.ValidateString(name, nameof(name));
        lock (_gate)
        {
            ThrowIfDisposed();
            NativeCall.Succeeded(NativeMethods.StepsComplete(_handle, name, value?.GetRawText()), "steps_complete");
        }
    }

    public void SetCursor(string name, JsonElement cursor)
    {
        NativeCall.ValidateString(name, nameof(name));
        lock (_gate)
        {
            ThrowIfDisposed();
            NativeCall.Succeeded(NativeMethods.StepsSetCursor(_handle, name, cursor.GetRawText()), "steps_set_cursor");
        }
    }

    public string ToJson()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var pointer = NativeMethods.StepsJson(_handle);
            return pointer == 0 ? throw NativeCall.Error("steps_json") : NativeCall.String(pointer)!;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

    public void Dispose()
    {
        lock (_gate)
            _handle.Dispose();
    }
}
