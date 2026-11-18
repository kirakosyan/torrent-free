namespace TorrentFree;

public partial class WindowsDonationPanel : ContentView
{
    private readonly AboutPage _host;

    public WindowsDonationPanel(AboutPage host)
    {
        InitializeComponent();
        _host = host;
    }

    private async void OnPatreonClicked(object? sender, EventArgs e) =>
        await _host.OpenExternalActionAsync(() => Browser.Default.OpenAsync(
            new Uri("https://www.patreon.com/16981794/join"), BrowserLaunchMode.External), "ErrorOpenBrowser");
}
