using TorrentFree.Services;

namespace TorrentFree.UnitTests;

internal sealed class CoreTestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "torrent-free-tests", Guid.NewGuid().ToString("N"));
    public StoragePaths StoragePaths => new(Path, System.IO.Path.Combine(Path, "Downloads"));
    public CoreTestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}
