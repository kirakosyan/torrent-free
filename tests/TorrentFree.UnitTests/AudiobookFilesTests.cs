using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AudiobookFilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "audiobooks-" + Guid.NewGuid());

    [Theory]
    [InlineData("Book.M4B", true)]
    [InlineData("Track.mp3", true)]
    [InlineData("Track.m4a", true)]
    [InlineData("Track.aac", true)]
    [InlineData("Track.FLAC", true)]
    [InlineData("Track.ogg", true)]
    [InlineData("Track.wav", true)]
    [InlineData("Book.mb4", false)]
    [InlineData("Book.mp3.exe", false)]
    [InlineData("Movie.mp4", false)]
    public void RecognizesOnlySupportedAudio(string name, bool supported) =>
        Assert.Equal(supported, AudiobookFiles.IsSupported(name));

    [Fact]
    public void SelectsAllChaptersButNoArtworkOrSiblingDownloads()
    {
        Directory.CreateDirectory(Path.Combine(root, "Book", "Disc 2"));
        var first = Path.Combine(root, "Book", "01.MP3");
        var second = Path.Combine(root, "Book", "Disc 2", "02.flac");
        foreach (var file in new[] { first, second, Path.Combine(root, "Book", "cover.jpg"), Path.Combine(root, "Other.m4b") })
            File.WriteAllText(file, "test");
        Assert.Equal(new[] { first, second }.Order(), AudiobookFiles.Enumerate(Path.Combine(root, "Book")).Order());
        Assert.Equal([first], AudiobookFiles.Enumerate(first));
        File.Delete(first);
        Assert.Empty(AudiobookFiles.Enumerate(first));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
