using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Stampeded.Interop;

// Signatures mirror the published stampeded-ffi crate's include/stampeded.h.
internal static partial class NativeMethods
{
    private const string Library = "stampeded_ffi";

    static NativeMethods() => NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveLibrary);

    private static nint ResolveLibrary(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var path = Environment.GetEnvironmentVariable("STAMPEDED_NATIVE_LIBRARY");
        return name == Library && !string.IsNullOrWhiteSpace(path)
            ? NativeLibrary.Load(Path.GetFullPath(path))
            : 0; // Let .NET resolve the current RID's NuGet native asset or the system library.
    }

    [LibraryImport(Library, EntryPoint = "stampeded_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint Version();

    [LibraryImport(Library, EntryPoint = "stampeded_last_error")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint LastError();

    [LibraryImport(Library, EntryPoint = "stampeded_log_init", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int LogInitialize(string? filter);

    [LibraryImport(Library, EntryPoint = "stampeded_connect", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint Connect(string databaseUrl);

    [LibraryImport(Library, EntryPoint = "stampeded_client_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ClientFree(nint client);

    [LibraryImport(Library, EntryPoint = "stampeded_enqueue", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Enqueue(ClientHandle client, string queue, string task, string arguments, in NativeEnqueueOptions options, out GuidBuffer id);

    [LibraryImport(Library, EntryPoint = "stampeded_puller_start")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint PullerStart(ClientHandle client, in NativePullerOptions options);

    [LibraryImport(Library, EntryPoint = "stampeded_puller_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int PullerNext(PullerHandle puller, uint timeoutMilliseconds, out nint lease);

    [LibraryImport(Library, EntryPoint = "stampeded_puller_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int PullerStop(PullerHandle puller, uint drainMilliseconds);

    [LibraryImport(Library, EntryPoint = "stampeded_puller_worker_id")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint PullerWorkerId(PullerHandle puller);

    [LibraryImport(Library, EntryPoint = "stampeded_puller_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void PullerFree(nint puller);

    [LibraryImport(Library, EntryPoint = "stampeded_lease_job")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint LeaseJob(LeaseHandle lease);

    [LibraryImport(Library, EntryPoint = "stampeded_lease_checkpoint", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int LeaseCheckpoint(LeaseHandle lease, string state);

    [LibraryImport(Library, EntryPoint = "stampeded_lease_complete")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int LeaseComplete(nint lease);

    [LibraryImport(Library, EntryPoint = "stampeded_lease_fail", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int LeaseFail(nint lease, string message, long retryMilliseconds);

    [LibraryImport(Library, EntryPoint = "stampeded_lease_requeue")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int LeaseRequeue(nint lease);

    [LibraryImport(Library, EntryPoint = "stampeded_lease_timed_out", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int LeaseTimedOut(nint lease, string message);

    [LibraryImport(Library, EntryPoint = "stampeded_lease_discard", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int LeaseDiscard(nint lease, string message);

    [LibraryImport(Library, EntryPoint = "stampeded_lease_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void LeaseFree(nint lease);

    [LibraryImport(Library, EntryPoint = "stampeded_steps_load", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint StepsLoad(string? checkpoint);

    [LibraryImport(Library, EntryPoint = "stampeded_steps_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void StepsFree(nint steps);

    [LibraryImport(Library, EntryPoint = "stampeded_steps_completed", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int StepsCompleted(StepsHandle steps, string name);

    [LibraryImport(Library, EntryPoint = "stampeded_steps_value", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint StepsValue(StepsHandle steps, string name);

    [LibraryImport(Library, EntryPoint = "stampeded_steps_cursor", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint StepsCursor(StepsHandle steps, string name);

    [LibraryImport(Library, EntryPoint = "stampeded_steps_complete", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int StepsComplete(StepsHandle steps, string name, string? value);

    [LibraryImport(Library, EntryPoint = "stampeded_steps_set_cursor", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int StepsSetCursor(StepsHandle steps, string name, string cursor);

    [LibraryImport(Library, EntryPoint = "stampeded_steps_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint StepsJson(StepsHandle steps);
}
