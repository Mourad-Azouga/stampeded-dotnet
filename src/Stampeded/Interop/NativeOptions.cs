using System.Runtime.InteropServices;

namespace Stampeded.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEnqueueOptions
{
    internal nint Metadata;
    internal nint UniqueKey;
    internal short Priority;
    internal short MaxAttempts;
    internal long ScheduledAtUnixMilliseconds;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePullerOptions
{
    internal nint Queues;
    internal nint ExceptQueues;
    internal uint MaxLeases;
    internal uint BatchSize;
    internal uint PollIntervalMilliseconds;
    internal uint HeartbeatIntervalMilliseconds;
}

// The C API writes exactly 36 ASCII UUID characters and a NUL terminator.
[StructLayout(LayoutKind.Sequential, Size = 37)]
internal unsafe struct GuidBuffer
{
    private fixed byte _bytes[37];

    internal readonly Guid ToGuid()
    {
        fixed (byte* bytes = _bytes)
            return Guid.Parse(System.Text.Encoding.ASCII.GetString(new ReadOnlySpan<byte>(bytes, 36)));
    }
}
