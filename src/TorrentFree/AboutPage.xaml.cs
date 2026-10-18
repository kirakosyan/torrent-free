using Microsoft.Maui.ApplicationModel;
using TorrentFree.Services;

namespace TorrentFree;

public partial class AboutPage : ContentPage
{
    private static readonly Uri PatreonUri = new("https://patreon.com/ArmenKirakosyan");
    private readonly Uri _sourceUri;
    private bool _openingLink;
    private bool _closing;

    public AboutPage(string aboutMessage, string sourceUrl)
    {
        InitializeComponent();
        AboutMessageLabel.Text = aboutMessage;
        _sourceUri = new Uri(sourceUrl);
    }

    private async void OnPatreonClicked(object? sender, EventArgs e) =>
        await OpenInBrowserAsync(PatreonUri);

    private async void OnSourceClicked(object? sender, EventArgs e) =>
        await OpenInBrowserAsync(_sourceUri);

    private async Task OpenInBrowserAsync(Uri uri)
    {
        if (_openingLink) return;
        _openingLink = true;
        try
        {
            if (await Browser.Default.OpenAsync(uri, BrowserLaunchMode.External)) return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening external browser failed: {ex}");
        }
        finally
        {
            _openingLink = false;
        }

        try
        {
            await DisplayAlertAsync(
                LocalizationResourceManager.Instance["AboutTitle"],
                LocalizationResourceManager.Instance["ErrorOpenBrowser"],
                LocalizationResourceManager.Instance["OK"]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Showing browser error failed: {ex}");
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (_closing) return;
        _closing = true;
        try
        {
            if (Navigation.ModalStack.LastOrDefault() == this)
                await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Closing About page failed: {ex}");
        }
        finally
        {
            _closing = false;
        }
    }
}
