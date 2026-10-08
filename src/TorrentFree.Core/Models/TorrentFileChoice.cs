using CommunityToolkit.Mvvm.ComponentModel;

namespace TorrentFree.Models;

/// <summary>A detached file choice: editing a dialog cannot change a running transfer.</summary>
public partial class TorrentFileChoice(string path, long length, bool isSelected = true) : ObservableObject
{
    public string Path { get; } = path.Replace('\\', '/');
    public long Length { get; } = length;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => Path.Contains('/') ? Path[..Path.LastIndexOf('/')] : string.Empty;
    public bool HasFolder => Folder.Length > 0;
    public string SizeLabel => TorrentItem.FormatBytes(Length);

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}
