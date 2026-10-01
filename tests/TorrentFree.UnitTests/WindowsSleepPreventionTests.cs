using System.Runtime.InteropServices;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class WindowsSleepPreventionTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact]
    public void ReasonContext_MatchesNativeLayout()
    {
        Assert.Equal(nint.Size == 8 ? 32 : 24, Marshal.SizeOf<WindowsSleepPreventionService.ReasonContext>());
        Assert.Equal((nint)8, Marshal.OffsetOf<WindowsSleepPreventionService.ReasonContext>(nameof(WindowsSleepPreventionService.ReasonContext.SimpleReasonString)));
    }

    [Fact(Skip = "Requires Windows power request APIs", SkipUnless = nameof(IsWindows))]
    public void NativeRequest_IsIdempotentAndReleasesHandleOnCompletionAndDisposal()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var service = new WindowsSleepPreventionService();
        service.SetPreventSleep(false);
        Assert.False(service.IsHeld);
        service.SetPreventSleep(true);
        Assert.True(service.IsHeld);
        service.SetPreventSleep(true);
        Assert.True(service.IsHeld);

        service.SetPreventSleep(false);
        Assert.False(service.IsHeld);
        service.SetPreventSleep(true);
        Assert.True(service.IsHeld);
        service.Dispose();
        Assert.False(service.IsHeld);
        service.SetPreventSleep(true);
        Assert.False(service.IsHeld);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedAcquire_RetriesOnlyAfterRelease(bool throws)
    {
        var attempts = 0;
        using var service = new WindowsSleepPreventionService(() =>
        {
            attempts++;
            if (throws) throw new InvalidOperationException("Power requests unavailable");
            return null;
        });
        for (var i = 0; i < 100; i++) service.SetPreventSleep(true);
        Assert.Equal(1, attempts);
        Assert.False(service.IsHeld);

        service.SetPreventSleep(false);
        service.SetPreventSleep(true);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Request_IsAcquiredOnceAndDisposedOncePerDownloadSession()
    {
        var requests = new List<RecordingRequest>();
        using var service = new WindowsSleepPreventionService(() =>
        {
            var request = new RecordingRequest();
            requests.Add(request);
            return request;
        });
        service.SetPreventSleep(true);
        service.SetPreventSleep(true);
        Assert.Single(requests);
        Assert.Equal(0, requests[0].Disposals);
        service.SetPreventSleep(false);
        service.SetPreventSleep(false);
        Assert.Equal(1, requests[0].Disposals);

        service.SetPreventSleep(true);
        Assert.Equal(2, requests.Count);
        service.Dispose();
        service.Dispose();
        service.SetPreventSleep(true);
        Assert.Equal(2, requests.Count);
        Assert.Equal(1, requests[1].Disposals);
    }

    private sealed class RecordingRequest : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
}
