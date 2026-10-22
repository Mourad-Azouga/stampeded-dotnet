using Stampeded.Interop;

namespace Stampeded;

public static class StampededRuntime
{
    /// <summary>The loaded C ABI library version, distinct from the Rust core and NuGet versions.</summary>
    public static string Version => NativeCall.String(NativeMethods.Version())
        ?? throw new InvalidOperationException("The native library did not return its version.");

    /// <summary>Enable upstream logging to stderr. Returns false when a logger already exists.</summary>
    public static bool InitializeLogging(string? filter = null)
    {
        if (filter is not null)
            NativeCall.ValidateString(filter, nameof(filter));
        return NativeCall.Succeeded(NativeMethods.LogInitialize(filter), "log_init");
    }
}
