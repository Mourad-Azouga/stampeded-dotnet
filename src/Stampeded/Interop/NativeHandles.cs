using Microsoft.Win32.SafeHandles;

namespace Stampeded.Interop;

internal sealed class ClientHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // SafeHandle parameters keep the native object alive for the entire P/Invoke call.
    internal ClientHandle(nint pointer) : base(true) => SetHandle(pointer);
    protected override bool ReleaseHandle() { NativeMethods.ClientFree(handle); return true; }
}

internal sealed class PullerHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal PullerHandle(nint pointer) : base(true) => SetHandle(pointer);
    protected override bool ReleaseHandle() { NativeMethods.PullerFree(handle); return true; }
}

internal sealed class LeaseHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal LeaseHandle(nint pointer) : base(true) => SetHandle(pointer);
    protected override bool ReleaseHandle() { NativeMethods.LeaseFree(handle); return true; }
}

internal sealed class StepsHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal StepsHandle(nint pointer) : base(true) => SetHandle(pointer);
    protected override bool ReleaseHandle() { NativeMethods.StepsFree(handle); return true; }
}
