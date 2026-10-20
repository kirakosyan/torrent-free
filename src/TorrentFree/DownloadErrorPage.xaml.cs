namespace TorrentFree;

public partial class DownloadErrorPage : ContentPage
{
    private readonly string _error;
    private bool _closing;

    public DownloadErrorPage(string torrentName, string error)
    {
        InitializeComponent();
        TorrentNameLabel.Text = torrentName;
        ErrorDetailsLabel.Text = _error = error;
    }

    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        CopyButton.IsEnabled = false;
        CopyFailureLabel.IsVisible = false;
        try
        {
            await Clipboard.Default.SetTextAsync(_error);
            CopyButton.Text = Services.LocalizationResourceManager.Instance["CopyErrorDetails"] + " ✓";
        }
        catch (Exception ex)
        {
            CopyFailureLabel.Text = ex.Message;
            CopyFailureLabel.IsVisible = true;
            CopyButton.IsEnabled = true;
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (_closing) return;
        _closing = true;
        try
        {
            if (Navigation.ModalStack.LastOrDefault() == this) await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Closing download error details failed: {ex}");
        }
        finally { _closing = false; }
    }
}
