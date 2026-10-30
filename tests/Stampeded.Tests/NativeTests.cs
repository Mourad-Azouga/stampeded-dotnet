using System.Runtime.InteropServices;
using System.Text.Json;
using Stampeded.Interop;

namespace Stampeded.Tests;

public sealed class NativeTests
{
    [Fact]
    public void LoadsTheActualUpstreamLibrary() => Assert.Equal("0.4.1", StampededRuntime.Version);

    [Fact]
    public void OptionsMatchThe64BitCLayout()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(32, Marshal.SizeOf<NativeEnqueueOptions>());
        Assert.Equal(24, Marshal.OffsetOf<NativeEnqueueOptions>(nameof(NativeEnqueueOptions.ScheduledAtUnixMilliseconds)).ToInt32());
        Assert.Equal(32, Marshal.SizeOf<NativePullerOptions>());
        Assert.Equal(16, Marshal.OffsetOf<NativePullerOptions>(nameof(NativePullerOptions.MaxLeases)).ToInt32());
        Assert.Equal(37, Marshal.SizeOf<GuidBuffer>());
    }

    [Fact]
    public void CopiesTheNativeErrorBeforeAnotherCallClearsIt()
    {
        var exception = Assert.Throws<StampededException>(() => StampededClient.Connect("not a postgres url"));
        Assert.Equal("connect", exception.Operation);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        _ = StampededRuntime.Version;
        Assert.Contains("connect", exception.Message);
    }

    [Fact]
    public void RejectsEmbeddedNulBeforeCrossingTheAbi() =>
        Assert.Throws<ArgumentException>(() => StampededClient.Connect("postgres://local\0host/db"));

    [Fact]
    public void StepsRoundTripUnicodeValuesAndCursorsThroughRust()
    {
        using var steps = JobSteps.Load();
        steps.SetCursor("render", JsonSerializer.SerializeToElement(new { page = 2 }));
        Assert.Equal(2, steps.GetCursor("render")!.Value.GetProperty("page").GetInt32());
        var value = JsonSerializer.SerializeToElement(new { path = "résumé/مرحبا.pdf" });
        steps.Complete("render", value);
        using var restored = JobSteps.Load(steps.ToJson());
        Assert.True(restored.IsCompleted("render"));
        Assert.False(restored.IsCompleted("send"));
        Assert.Null(restored.GetCursor("render"));
        Assert.Equal(value.GetProperty("path").GetString(), restored.GetValue("render")!.Value.GetProperty("path").GetString());
    }

    [Fact]
    public void InvalidStepInputPreservesTheUpstreamError() =>
        Assert.Throws<StampededException>(() => JobSteps.Load("{invalid"));

    [Fact]
    public void DisposedStepsCannotBeUsedAgain()
    {
        var steps = JobSteps.Load();
        steps.Dispose();
        steps.Dispose();
        Assert.Throws<ObjectDisposedException>(() => steps.IsCompleted("send"));
    }

    [Fact]
    public void RejectsUnsupportedQueueSelection()
    {
        Assert.Throws<ArgumentException>(() => new PullerOptions { Queues = ["mail,other"] }.Validate());
        Assert.Throws<ArgumentException>(() => new PullerOptions { ExceptQueues = ["dead"] }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new PullerOptions { MaxLeases = 0 }.Validate());
    }
}
