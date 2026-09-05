using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class DownloadExportServiceTests
{
    [Fact]
    public async Task DifferentOwnersAndSameSizeNames_KeepBothExports()
    {
        using var directory = new CoreTestDirectory();
        var store = new RecordingExportStore();
        var service = new DownloadExportService(Path.Combine(directory.Path, "state"), store);
        var source = Path.Combine(directory.Path, "file.bin");
        await File.WriteAllTextAsync(source, "AAAA", TestContext.Current.CancellationToken);
        var first = await service.ExportAsync("torrent-a", source, "Downloads/TorrentFree/");
        await File.WriteAllTextAsync(source, "BBBB", TestContext.Current.CancellationToken);
        var second = await service.ExportAsync("torrent-b", source, "Downloads/TorrentFree/");
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
        var first = await new DownloadExportService(state, store).ExportAsync("owner", source, "folder");
        var restarted = new DownloadExportService(state, store);
        Assert.Equal(first, await restarted.ExportAsync("owner", source, "folder"));
        Assert.Single(store.Completed);
        store.Completed[first] = "BBBB"u8.ToArray();
        var replacement = await restarted.ExportAsync("owner", source, "folder");
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
        var first = await service.ExportAsync("owner", source, "folder");
        await File.WriteAllTextAsync(source, replacement, TestContext.Current.CancellationToken);
        store.FailCompletion = true;
        await Assert.ThrowsAsync<IOException>(() => service.ExportAsync("owner", source, "folder"));
        Assert.Equal("AAAA", store.ReadText(first));
        Assert.DoesNotContain(first, store.Aborted);
        Assert.Single(store.Aborted);
        store.FailCompletion = false;
        var second = await service.ExportAsync("owner", source, "folder");
        Assert.Equal(replacement, store.ReadText(second));
        Assert.Equal(2, store.Completed.Count);
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
