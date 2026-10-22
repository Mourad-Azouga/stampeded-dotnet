using System.Runtime.InteropServices;

namespace Stampeded.Interop;

internal static class NativeCall
{
    internal static bool Succeeded(int status, string operation) => status switch
    {
        0 => true,
        1 => false,
        -1 => throw Error(operation),
        _ => throw new InvalidOperationException($"Unexpected native status {status} from {operation}.")
    };

    // Call before any await or other native call: the error is thread-local and temporary.
    internal static StampededException Error(string operation) =>
        new(operation, String(NativeMethods.LastError()) ?? "The native library returned no error details.");

    internal static string? String(nint pointer) => pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);

    internal static void ValidateString(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Contains('\0'))
            throw new ArgumentException("Native strings cannot contain a NUL character.", name);
    }

    internal static uint Milliseconds(TimeSpan value, string name, bool allowZero = false)
    {
        if (value < TimeSpan.Zero || (!allowZero && value == TimeSpan.Zero) || value.TotalMilliseconds > uint.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Duration must fit an unsigned 32-bit millisecond count.");
        return checked((uint)Math.Ceiling(value.TotalMilliseconds));
    }
}

internal sealed class Utf8String : IDisposable
{
    internal nint Pointer { get; }

    internal Utf8String(string? value)
    {
        if (value is not null)
        {
            if (value.Contains('\0'))
                throw new ArgumentException("Native strings cannot contain a NUL character.", nameof(value));
            Pointer = Marshal.StringToCoTaskMemUTF8(value);
        }
    }

    public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
}
