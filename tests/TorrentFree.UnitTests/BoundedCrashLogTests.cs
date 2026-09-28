using System.Text;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class BoundedCrashLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TorrentFreeCrashLogTests", Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(_directory, "crash.log");

    [Fact]
    public void Append_RotatesAndKeepsOnlyOnePreviousLog()
    {
        var log = new BoundedCrashLog(LogPath, 128);
        var first = new string('a', 100);
        var second = new string('b', 100);
        log.Append(first);
        log.Append(second);
        log.Append("latest crash\n");
        Assert.Equal(first, File.ReadAllText(LogPath + ".1"));
        Assert.Equal(second + "latest crash\n", File.ReadAllText(LogPath));

        log.Append(new string('c', 100));
        Assert.Equal(second + "latest crash\n", File.ReadAllText(LogPath + ".1"));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void Append_LargeUnicodeEntryRemainsBoundedAndValidUtf8()
    {
        var log = new BoundedCrashLog(LogPath, 128);
        log.Append(string.Concat(Enumerable.Repeat("🔥", 100)));
        var bytes = File.ReadAllBytes(LogPath);
        Assert.InRange(bytes.Length, 1, 128);
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.EndsWith("[entry truncated]\n", text);
    }

    [Fact]
    public void Append_OversizedLegacyLogKeepsBoundedTail()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LogPath, string.Concat(Enumerable.Repeat("🔥", 100)) + "last crash\n");
        new BoundedCrashLog(LogPath, 128).Append("new crash\n");

        var previous = File.ReadAllBytes(LogPath + ".1");
        Assert.InRange(previous.Length, 1, 128);
        Assert.EndsWith("last crash\n", new UTF8Encoding(false, true).GetString(previous));
        Assert.Equal("new crash\n", File.ReadAllText(LogPath));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
