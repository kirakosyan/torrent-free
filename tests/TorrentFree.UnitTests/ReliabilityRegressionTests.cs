using System.Globalization;
using System.Text;
using MonoTorrent.Client;
using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class ReliabilityRegressionTests
{
    [Theory]
    [InlineData("١٢٣", false, "en-US", "123", 123)]
    [InlineData("１２３", false, "en-US", "123", 123)]
    [InlineData("𝟙𝟚", false, "en-US", "12", 12)]
    [InlineData("١٫٥", true, "nb-NO", "1,5", 1.5)]
    [InlineData("1,5", true, "en-US", "1.5", 1.5)]
    [InlineData("1.", true, "en-US", "1.", 1)]
    [InlineData("", false, "en-US", "", 0)]
    [InlineData("not a number", false, "en-US", "", 0)]
    [InlineData("999999999999999999999", false, "en-US", "1000000", 1000000)]
    public void NumericInput_DisplaysTheValueThatWillBeSaved(string input, bool decimalInput, string culture, string text, double value)
    {
        var parsed = NumericInput.Parse(input, decimalInput, 1000000, CultureInfo.GetCultureInfo(culture));
        Assert.Equal(text, parsed.Text);
        Assert.Equal(value, parsed.Value);
    }

    [Theory]
    [InlineData(99.5, 99)]
    [InlineData(99.99, 99)]
    [InlineData(100, 100)]
    [InlineData(double.NaN, 0)]
    public void Progress_DoesNotRoundUpToCompletion(double progress, double expected)
        => Assert.Equal(expected, new TorrentItem { Progress = progress }.DisplayProgress);

    [Fact]
    public void CompletedSeeds_AllowExplicitStartButNotBulkRestart()
    {
        var item = new TorrentItem { Status = DownloadStatus.Completed };
        Assert.True(item.CanStart);
        Assert.False(item.CanStartInBulk);
        item.Status = DownloadStatus.Paused;
        Assert.True(item.CanStartInBulk);
    }

    [Fact]
    public void ForegroundWork_IsDeferredAndSupersededActivationsAreDiscarded()
    {
        var posted = new Queue<Action>();
        var applied = new List<bool>();
        var updates = new DeferredForegroundUpdates(posted.Enqueue, foreground =>
        {
            applied.Add(foreground);
            return Task.CompletedTask;
        });
        updates.Request(true);
        updates.Request(false);
        Assert.Empty(applied);
        while (posted.TryDequeue(out var action)) action();
        Assert.Equal([false], applied);
        updates.Request(true);
        posted.Dequeue()();
        Assert.Equal([false, true], applied);
    }

    [Fact]
    public void Diagnostics_KeepRecentLifecycleAndManagedExceptionDetails()
    {
        using var directory = new CoreTestDirectory();
        var path = Path.Combine(directory.Path, "crash.log");
        var log = new DiagnosticLog(path, "app=1.24; runtime=test");
        for (var i = 0; i < 40; i++) log.Breadcrumb("Window.Event-" + i.ToString("D2"));
        log.Write("WinUI.UnhandledException", new InvalidOperationException("activation failure", new IOException("inner failure")));
        var content = File.ReadAllText(path);
        Assert.Contains("HRESULT=0x80131509", content);
        Assert.Contains("inner failure", content);
        Assert.Contains("app=1.24", content);
        Assert.Contains("Window.Event-39", content);
        Assert.DoesNotContain("Window.Event-00", content);
        // A filesystem failure must not mask the original crash.
        new DiagnosticLog(directory.Path, "test").Write("failure", new Exception("original"));
    }

    [Fact]
    public void Parser_RetainsPrivateFlagAndRawInfoHash()
    {
        var info = "d4:name3:abc6:lengthi123e7:privatei1ee";
        var metadata = new TorrentFileParser().Parse(Encoding.ASCII.GetBytes("d4:info" + info + "e"));
        Assert.True(metadata.IsPrivate);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.ASCII.GetBytes(info))).ToLowerInvariant(), metadata.InfoHashHex);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task MissingPrivateOrLegacyMetadata_DoesNotRegisterAPublicMagnet(bool? isPrivate)
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var item = new TorrentItem
        {
            MagnetLink = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
            ImportedFromFile = true, IsPrivate = isPrivate,
            SavePath = fixture.Storage.GetDefaultDownloadPath(), Status = DownloadStatus.Paused
        };
        fixture.Service.Torrents.Add(item);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.StartTorrentAsync(item));
        Assert.Equal(LocalizationResourceManager.Instance["ErrorPrivateTorrentMetadata"], error.Message);
        Assert.Empty(fixture.Engine.Torrents);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetTorrentFilesAsync(item, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Engine.Torrents);
    }

    [Fact]
    public async Task PrivateImport_StartsWithPrivateMetadata_AndCannotFallBackAfterCacheLoss()
    {
        await using var fixture = new CoreServiceFixture();
        await fixture.Service.InitializeAsync();
        var metadata = await fixture.PrepareTorrentAsync(isPrivate: true);
        var item = (await fixture.Service.AddTorrentFileAsync(metadata))!;
        Assert.True(item.IsPrivate);
        await fixture.Service.StartTorrentAsync(item);
        var manager = Assert.Single(fixture.Engine.Torrents);
        Assert.True(manager.Torrent!.IsPrivate);
        Assert.True(manager.TrackerManager.Private);
        await fixture.Service.StopTorrentAsync(item);
        // A new process has no manager to recover metadata from.
        await fixture.Service.DisposeAsync();
        File.Delete(item.CachedTorrentFilePath!);
        await using var restoredFixture = new CoreServiceFixture();
        item.Status = DownloadStatus.Paused;
        restoredFixture.Service.Torrents.Add(item);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restoredFixture.Service.StartTorrentAsync(item));
        Assert.Empty(restoredFixture.Engine.Torrents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRollback_RetryRecoversRegisteredOrDetachedManager(bool detachBeforeCleanupFailure)
    {
        await using var fixture = new CoreServiceFixture(serviceFactory: (storage, notifications, background, time) =>
            new FailFirstStartAndStop(storage, notifications, background, time, detachBeforeCleanupFailure));
        await fixture.Service.InitializeAsync();
        var item = (await fixture.Service.AddTorrentFileAsync(await fixture.PrepareTorrentAsync()))!;
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.StartTorrentAsync(item));
        var registered = fixture.Engine.Torrents.SingleOrDefault();
        Assert.Equal(detachBeforeCleanupFailure, registered is null);
        Assert.Equal(DownloadStatus.Failed, item.Status);
        await fixture.Service.StartTorrentAsync(item);
        await CoreServiceFixture.WaitUntilAsync(() => item.Status == DownloadStatus.Seeding);
        var recovered = Assert.Single(fixture.Engine.Torrents);
        if (!detachBeforeCleanupFailure) Assert.Same(registered, recovered);
        Assert.Null(item.ErrorMessage);
    }

    private sealed class FailFirstStartAndStop(IStorageService storage, INotificationService notifications,
        IBackgroundDownloadService background, TimeProvider time, bool detachBeforeCleanupFailure)
        : TorrentService(storage, notifications, background, ImmediateDispatcher.Instance, time)
    {
        private bool _failedStart;
        private bool _failedStop;
        protected override Task StartManagerAsync(TorrentManager manager)
        {
            if (!_failedStart) { _failedStart = true; throw new IOException("Injected start failure"); }
            return base.StartManagerAsync(manager);
        }
        protected override async Task StopManagerAsync(TorrentManager manager)
        {
            if (!_failedStop)
            {
                _failedStop = true;
                if (detachBeforeCleanupFailure) await manager.Engine!.RemoveAsync(manager, RemoveMode.KeepAllData);
                throw new IOException("Injected cleanup failure");
            }
            await base.StopManagerAsync(manager);
        }
    }
}
