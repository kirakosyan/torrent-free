using TorrentFree.Services;

namespace TorrentFree;

public partial class AboutPage : ContentPage
{
    private readonly IAppStore _appStore;
    private bool _openingLink;
    private bool _closing;

    public AboutPage(string aboutMessage, IAppStore appStore)
    {
        InitializeComponent();
        _appStore = appStore;
        AboutMessageLabel.Text = aboutMessage;
#if WINDOWS
        AboutActions.Add(new WindowsDonationPanel(this));
#endif
#if !WINDOWS && !ANDROID
        FeedbackButton.IsVisible = false;
#endif
    }

    private async void OnFeedbackClicked(object? sender, EventArgs e) =>
        await OpenExternalActionAsync(() => _appStore.OpenReviewPageAsync(), "StoreActionError");

    internal async Task OpenExternalActionAsync(Func<Task<bool>> open, string errorResource)
    {
        if (_openingLink) return;
        _openingLink = true;
        FeedbackButton.IsEnabled = false;
        try
        {
            if (await open()) return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening About page link failed: {ex}");
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
                LocalizationResourceManager.Instance[errorResource],
                LocalizationResourceManager.Instance["OK"]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Showing About page link error failed: {ex}");
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
