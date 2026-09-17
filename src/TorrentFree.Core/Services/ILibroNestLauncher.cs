namespace TorrentFree.Services;

public interface ILibroNestLauncher
{
    bool IsSupported { get; }
    Task OpenAsync(string downloadPath, IReadOnlyList<string> audioFiles);
}
