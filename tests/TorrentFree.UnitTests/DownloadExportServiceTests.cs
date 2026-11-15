using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DownloadExportServiceTests
{
    [Fact]
    public async Task CancelCopy_AbortsPartialDestinationAndAllowsRetry()
    {
        using var directory = new CoreTestDirectory();
        var store = new RecordingExportStore();
        var service = new DownloadExportService(Path.Combine(directory.Path, "state"), store);
        var source = Path.Combine(directory.Path, "large.bin");
        await File.WriteAllBytesAsync(source, new byte[2 * 1024 * 1024], TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new ImmediateProgress(value => { if (value.Bytes > 0) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync("owner", source, "folder", cancellation.Token, progress));
        Assert.Single(store.Aborted);
        Assert.Empty(store.Completed);
        Assert.Empty(Directory.GetFiles(Path.Combine(directory.Path, "state"), "*.json"));
        await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);
        Assert.Single(store.Completed);
    }

    [Fact]
    public async Task CancelVerification_DoesNotCreateOrDeleteAnyCopies()
    {
        using var directory = new CoreTestDirectory();
        var store = new RecordingExportStore();
        var service = new DownloadExportService(Path.Combine(directory.Path, "state"), store);
        var source = Path.Combine(directory.Path, "large.bin");
        await File.WriteAllBytesAsync(source, new byte[2 * 1024 * 1024], TestContext.Current.CancellationToken);
        var original = await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new ImmediateProgress(value =>
        {
            Assert.Equal(DownloadExportPhase.Verifying, value.Phase);
            if (value.Bytes > 0) cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync("owner", source, "folder", cancellation.Token, progress));
        Assert.Single(store.Completed);
        Assert.Empty(store.Aborted);
        Assert.Equal(original, await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InsufficientSpace_PreventsCopyButStillAllowsVerifiedReuse()
    {
        using var directory = new CoreTestDirectory();
        var store = new RecordingExportStore();
        long free = 100;
        var service = new DownloadExportService(Path.Combine(directory.Path, "state"), store, () => free);
        var source = Path.Combine(directory.Path, "file.bin");
        await File.WriteAllTextAsync(source, "data", TestContext.Current.CancellationToken);
        var original = await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);
        free = 0;
        Assert.Equal(original, await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<DownloadExportSpaceException>(() => service.ExportAsync("new-owner", source, "folder", TestContext.Current.CancellationToken));
        Assert.Single(store.Completed);
        Assert.Empty(store.Aborted);
    }

    [Fact]
    public void ExportPlan_IncludesOnlySelectedMetadataFiles()
    {
        using var directory = new CoreTestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "selected.bin"), "data");
        File.WriteAllText(Path.Combine(directory.Path, "skipped.bin"), "partial");
        File.WriteAllText(Path.Combine(directory.Path, "unrelated.bin"), "unrelated");
        var plan = DownloadExportPlan.Create(directory.Path, true,
            [new("selected.bin", 4), new("skipped.bin", 100, false)]);
        Assert.EndsWith("selected.bin", Assert.Single(plan.Files).SourcePath);
        Assert.Equal(4, plan.TotalBytes);
        Assert.Throws<InvalidDataException>(() => DownloadExportPlan.Create(directory.Path, true, [new("../outside.bin", 0)]));
        Assert.Throws<InvalidDataException>(() => DownloadExportPlan.Create(directory.Path, true, [new("skipped.bin", 100)]));
    }

    private sealed class ImmediateProgress(Action<DownloadExportProgress> report) : IProgress<DownloadExportProgress>
    {
        public void Report(DownloadExportProgress value) => report(value);
    }

    [Fact]
    public async Task DifferentOwnersAndSameSizeNames_KeepBothExports()
    {
        using var directory = new CoreTestDirectory();
        var store = new RecordingExportStore();
        var service = new DownloadExportService(Path.Combine(directory.Path, "state"), store);
        var source = Path.Combine(directory.Path, "file.bin");
        await File.WriteAllTextAsync(source, "AAAA", TestContext.Current.CancellationToken);
        var first = await service.ExportAsync("torrent-a", source, "Downloads/TorrentFree/", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(source, "BBBB", TestContext.Current.CancellationToken);
        var second = await service.ExportAsync("torrent-b", source, "Downloads/TorrentFree/", TestContext.Current.CancellationToken);
        Assert.NotEqual(first, second);
        Assert.Equal("AAAA", store.ReadText(first));
        Assert.Equal("BBBB", store.ReadText(second));
        Assert.Empty(store.Aborted);
    }

    [Fact]
    public async Task Restart_ReusesOnlyOwnedDestinationWithVerifiedContent()
    {
        using var directory = new CoreTestDirectory();
        var store = new RecordingExportStore();
        var state = Path.Combine(directory.Path, "state");
        var source = Path.Combine(directory.Path, "file.bin");
        await File.WriteAllTextAsync(source, "AAAA", TestContext.Current.CancellationToken);
        var first = await new DownloadExportService(state, store).ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);
        var restarted = new DownloadExportService(state, store);
        Assert.Equal(first, await restarted.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken));
        Assert.Single(store.Completed);
        store.Completed[first] = "BBBB"u8.ToArray();
        var replacement = await restarted.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);
        Assert.NotEqual(first, replacement);
        Assert.Equal("AAAA", store.ReadText(replacement));
        Assert.Equal("BBBB", store.ReadText(first));
    }

    [Theory]
    [InlineData("BBBB")]
    [InlineData("longer replacement")]
    public async Task ReplacementFailure_AbortsOnlyNewDestination(string replacement)
    {
        using var directory = new CoreTestDirectory();
        var store = new RecordingExportStore();
        var service = new DownloadExportService(Path.Combine(directory.Path, "state"), store);
        var source = Path.Combine(directory.Path, "file.bin");
        await File.WriteAllTextAsync(source, "AAAA", TestContext.Current.CancellationToken);
        var first = await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);
        var originalWriteTime = File.GetLastWriteTimeUtc(source);
        await File.WriteAllTextAsync(source, replacement, TestContext.Current.CancellationToken);
        // A quick same-length rewrite can share a filesystem timestamp tick.
        File.SetLastWriteTimeUtc(source, originalWriteTime.AddMinutes(1));
        store.FailCompletion = true;
        await Assert.ThrowsAsync<IOException>(() => service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken));
        Assert.Equal("AAAA", store.ReadText(first));
        Assert.DoesNotContain(first, store.Aborted);
        Assert.Single(store.Aborted);
        store.FailCompletion = false;
        var second = await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);
        Assert.Equal(replacement, store.ReadText(second));
        Assert.Equal(2, store.Completed.Count);
    }

    [Fact]
    public async Task UnchangedSourceStamp_ReusesRecordedDigestWithoutRehashingSource()
    {
        using var directory = new CoreTestDirectory();
        var store = new RecordingExportStore();
        var service = new DownloadExportService(Path.Combine(directory.Path, "state"), store);
        var source = Path.Combine(directory.Path, "file.bin");
        await File.WriteAllTextAsync(source, "AAAA", TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(source);
        var first = await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);

        // Same length and write time: the recorded digest is trusted (like a quick size/mtime
        // check), so the multi-gigabyte source is not read again. Only the destination is verified.
        await File.WriteAllTextAsync(source, "ZZZZ", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(source, stamp);
        Assert.Equal(first, await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken));
        Assert.Single(store.Completed);

        // A changed write time is detected and exports the new content.
        File.SetLastWriteTimeUtc(source, stamp.AddMinutes(1));
        var second = await service.ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);
        Assert.NotEqual(first, second);
        Assert.Equal("ZZZZ", store.ReadText(second));
    }

    [Fact]
    public async Task LegacyManifestWithoutSourceStamp_IsStillReused()
    {
        using var directory = new CoreTestDirectory();
        var store = new RecordingExportStore();
        var state = Path.Combine(directory.Path, "state");
        var source = Path.Combine(directory.Path, "file.bin");
        await File.WriteAllTextAsync(source, "AAAA", TestContext.Current.CancellationToken);
        var first = await new DownloadExportService(state, store).ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken);
        var manifest = Assert.Single(Directory.GetFiles(state, "*.json"));
        var legacy = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(manifest, TestContext.Current.CancellationToken)).RootElement;
        await File.WriteAllTextAsync(manifest, System.Text.Json.JsonSerializer.Serialize(new
        {
            Id = legacy.GetProperty("Id").GetString(),
            Digest = legacy.GetProperty("Digest").GetString()
        }), TestContext.Current.CancellationToken);

        Assert.Equal(first, await new DownloadExportService(state, store).ExportAsync("owner", source, "folder", TestContext.Current.CancellationToken));
        Assert.Single(store.Completed);
    }

    private sealed class RecordingExportStore : IDownloadExportStore
    {
        private readonly Dictionary<string, MemoryStream> _pending = [];
        public Dictionary<string, byte[]> Completed { get; } = [];
        public List<string> Aborted { get; } = [];
        public bool FailCompletion { get; set; }
        public string ReadText(string id) => System.Text.Encoding.UTF8.GetString(Completed[id]);
        public Task<Stream?> OpenReadAsync(string id) => Task.FromResult<Stream?>(Completed.TryGetValue(id, out var data) ? new MemoryStream(data) : null);
        public Task<ExportWriteTarget> CreateAsync(string directory, string name)
        {
            var id = Guid.NewGuid().ToString();
            var stream = new MemoryStream();
            _pending.Add(id, stream);
            return Task.FromResult(new ExportWriteTarget(id, stream));
        }
        public Task CompleteAsync(string id)
        {
            if (FailCompletion) throw new IOException("Injected publication failure");
            Completed.Add(id, _pending[id].ToArray());
            _pending.Remove(id);
            return Task.CompletedTask;
        }
        public Task AbortAsync(string id)
        {
            Aborted.Add(id);
            _pending.Remove(id);
            Completed.Remove(id);
            return Task.CompletedTask;
        }
    }
}
