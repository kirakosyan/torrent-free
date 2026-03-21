namespace TorrentFree.Services;

/// <summary>
/// Lets the user select a folder path.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// Indicates whether the current platform supports an interactive folder picker.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Opens the platform folder picker and returns the selected folder path.
    /// </summary>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}