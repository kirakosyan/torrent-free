using TorrentFree.Models;
using TorrentFree.Services;

namespace TorrentFree.Controls;

/// <summary>Shows copy and verification progress; back cancels and waits for partial-file cleanup.</summary>
public sealed class DownloadExportPage : ContentPage
{
    private readonly CancellationTokenSource _cancellation;
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly ActivityIndicator _preparing = new() { IsRunning = true };

    public DownloadExportPage(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation;
        Title = LocalizationResourceManager.Instance["ExportDownload"];
        _status.Text = LocalizationResourceManager.Instance["ExportVerifying"];
        this.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#17202D"));
        _status.SetAppThemeColor(Label.TextColorProperty, Colors.Black, Colors.White);
        var cancel = new Button { Text = LocalizationResourceManager.Instance["Cancel"] };
        cancel.Clicked += (_, _) => { _cancellation.Cancel(); cancel.IsEnabled = false; };
        Content = new VerticalStackLayout
        {
            Padding = 24, Spacing = 20, VerticalOptions = LayoutOptions.Center,
            Children = { _status, _preparing, _progress, cancel }
        };
    }

    public void Report(int fileNumber, int fileCount, DownloadExportProgress progress)
    {
        _preparing.IsRunning = false;
        _preparing.IsVisible = false;
        var phase = LocalizationResourceManager.Instance[progress.Phase == DownloadExportPhase.Copying ? "ExportCopying" : "ExportVerifying"];
        _status.Text = $"{phase} ({fileNumber}/{fileCount})\n{TorrentItem.FormatBytes(progress.Bytes)} / {TorrentItem.FormatBytes(progress.TotalBytes)}";
        _progress.Progress = progress.TotalBytes == 0 ? 1 : Math.Clamp((double)progress.Bytes / progress.TotalBytes, 0, 1);
    }

    protected override bool OnBackButtonPressed() { _cancellation.Cancel(); return true; }
    protected override void OnDisappearing() { _cancellation.Cancel(); base.OnDisappearing(); }
}
