using Microsoft.Maui.ApplicationModel;
using TorrentFree.Services;

namespace TorrentFree;

public partial class AboutPage : ContentPage
{
    private static readonly Uri PatreonUri = new("https://www.patreon.com/16981794/join");
    private readonly IAppStore _appStore;
    private bool _openingLink;
    private bool _closing;

    public AboutPage(string aboutMessage, IAppStore appStore)
    {
        InitializeComponent();
        _appStore = appStore;
        AboutMessageLabel.Text = aboutMessage;
#if !WINDOWS && !ANDROID
        FeedbackButton.IsVisible = false;
#endif
    }

    private async void OnPatreonClicked(object? sender, EventArgs e) =>
        await OpenInBrowserAsync(PatreonUri);

    private async void OnFeedbackClicked(object? sender, EventArgs e)
    {
        if (_openingLink) return;
        _openingLink = true;
        FeedbackButton.IsEnabled = false;
        try
        {
            if (await _appStore.OpenReviewPageAsync()) return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening store feedback failed: {ex}");
        }
        finally
        {
            _openingLink = false;
            FeedbackButton.IsEnabled = true;
        }

        try
        {
            await DisplayAlertAsync(
                LocalizationResourceManager.Instance["AboutTitle"],
                LocalizationResourceManager.Instance["StoreActionError"],
                LocalizationResourceManager.Instance["OK"]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Showing store feedback error failed: {ex}");
        }
    }

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
