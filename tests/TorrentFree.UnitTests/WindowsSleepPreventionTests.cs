using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class WindowsSleepPreventionTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact]
    public void ReasonContext_MatchesNativeLayout()
    {
        var context = typeof(WindowsSleepPreventionService).GetNestedType("ReasonContext", BindingFlags.NonPublic)!;
        Assert.Equal(nint.Size == 8 ? 32 : 24, Marshal.SizeOf(context));
        Assert.Equal((nint)8, Marshal.OffsetOf(context, "SimpleReasonString"));
    }

    [Fact(Skip = "Requires Windows power request APIs", SkipUnless = nameof(IsWindows))]
    public void NativeRequest_IsIdempotentAndReleasesHandleOnCompletionAndDisposal()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var service = new WindowsSleepPreventionService();
        var requestField = typeof(WindowsSleepPreventionService).GetField("_request", BindingFlags.Instance | BindingFlags.NonPublic)!;
        service.SetPreventSleep(false);
        Assert.Null(requestField.GetValue(service));
        service.SetPreventSleep(true);
        var first = Assert.IsType<SafeFileHandle>(requestField.GetValue(service));
        Assert.False(first.IsInvalid);
        Assert.False(first.IsClosed);
        service.SetPreventSleep(true);
        Assert.Same(first, requestField.GetValue(service));

        service.SetPreventSleep(false);
        Assert.True(first.IsClosed);
        Assert.Null(requestField.GetValue(service));
        service.SetPreventSleep(true);
        var second = Assert.IsType<SafeFileHandle>(requestField.GetValue(service));
        Assert.NotSame(first, second);
        Assert.False(second.IsClosed);
        service.Dispose();
        Assert.True(second.IsClosed);
        Assert.Null(requestField.GetValue(service));
        service.SetPreventSleep(true);
        Assert.Null(requestField.GetValue(service));
    }
}
