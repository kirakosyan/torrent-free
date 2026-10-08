using TorrentFree.Models;
using TorrentFree.Services;

namespace TorrentFree;

public partial class TorrentFilesPage : ContentPage
{
    private readonly ITorrentService _service;
    private readonly TorrentItem _torrent;
    private readonly TorrentFileSelectionViewModel _viewModel = new();
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _loading;
    private bool _saving;
    private bool _loaded;
    private bool _accepted;
    private bool _closing;
    private (bool Compact, bool Wide, bool Minimal)? _layoutMode;

    public Task<bool> Result => _result.Task;

    public TorrentFilesPage(ITorrentService service, TorrentItem torrent, bool newDownload)
    {
        InitializeComponent();
        _service = service;
        _torrent = torrent;
        BindingContext = _viewModel;
        TorrentName.Text = torrent.Name;
        if (newDownload) ConfirmButton.Text = LocalizationResourceManager.Instance["DownloadSelectedFiles"];
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        PrepareForPresentation();
        if (!_loaded) await LoadFilesAsync();
    }

    internal void PrepareForPresentation() => ConfigurePlatformKeyboard();
    internal void FinishPresentation() => RestorePlatformKeyboard();
    partial void ConfigurePlatformKeyboard();
    partial void RestorePlatformKeyboard();

    private void OnLayoutSizeChanged(object sender, EventArgs args)
    {
        if (LayoutRoot.Width <= 0 || LayoutRoot.Height <= 0) return;
        var compact = LayoutRoot.Height < 540;
        var wide = compact && LayoutRoot.Width >= 600;
        var minimal = LayoutRoot.Height < 260;
        if (_layoutMode == (compact, wide, minimal)) return;
        _layoutMode = (compact, wide, minimal);

        // A landscape keyboard can leave only a narrow strip for search and results.
        // The keyboard's Search action dismisses it and restores the selection controls.
        HeaderLayout.IsVisible = FooterLayout.IsVisible = BulkActions.IsVisible = !minimal;
        LayoutRoot.Padding = minimal ? 8 : compact ? 12 : 20;
        LayoutRoot.RowSpacing = minimal ? 4 : compact ? 8 : 12;
        Heading.FontSize = compact ? 20 : 24;
        HelpLabel.IsVisible = !compact;
        TorrentName.MaxLines = compact ? 1 : 2;
        Grid.SetColumnSpan(Heading, wide ? 1 : 2);
        Grid.SetRow(TorrentName, wide ? 0 : 1);
        Grid.SetColumn(TorrentName, wide ? 1 : 0);
        Grid.SetColumnSpan(TorrentName, wide ? 1 : 2);

        Grid.SetColumnSpan(FileSearch, wide && !minimal ? 1 : 2);
        Grid.SetRow(BulkActions, wide ? 0 : 1);
        Grid.SetColumn(BulkActions, wide ? 1 : 0);
        Grid.SetColumnSpan(BulkActions, wide ? 1 : 2);

        SelectionNote.IsVisible = !compact;
        ScrollableNote.IsVisible = compact;
        FooterLayout.RowSpacing = compact ? 6 : 10;
        Grid.SetColumnSpan(SummaryLabel, wide ? 1 : 2);
        Grid.SetColumnSpan(SelectionError, wide ? 1 : 2);
        Grid.SetRow(ConfirmationActions, wide ? 0 : 3);
        Grid.SetColumn(ConfirmationActions, wide ? 1 : 0);
        Grid.SetColumnSpan(ConfirmationActions, wide ? 1 : 2);
    }

    private void OnSearchButtonPressed(object sender, EventArgs args) => FileSearch.Unfocus();

    private async Task LoadFilesAsync()
    {
        _loading?.Cancel();
        using var loading = new CancellationTokenSource();
        _loading = loading;
        LoadingPanel.IsVisible = LoadingIndicator.IsRunning = true;
        RetryButton.IsVisible = false;
        LoadingMessage.Text = LocalizationResourceManager.Instance["LoadingTorrentFiles"];
        try
        {
            var files = await _service.GetTorrentFilesAsync(_torrent, loading.Token);
            if (loading.IsCancellationRequested) return;
            _viewModel.Load(files);
            TorrentName.Text = _torrent.Name;
            _loaded = true;
            SelectionTools.IsVisible = FileList.IsVisible = SummaryLabel.IsVisible = true;
            LoadingPanel.IsVisible = false;
        }
        catch (Exception ex)
        {
            if (loading.IsCancellationRequested) return;
            System.Diagnostics.Debug.WriteLine($"Loading torrent file choices failed: {ex}");
            LoadingMessage.Text = LocalizationResourceManager.Instance["LoadTorrentFilesFailed"];
            RetryButton.IsVisible = true;
        }
        finally
        {
            if (ReferenceEquals(_loading, loading))
            {
                LoadingIndicator.IsRunning = false;
                _loading = null;
            }
        }
    }

    private async void OnRetryClicked(object sender, EventArgs args) => await LoadFilesAsync();

    private async void OnConfirmClicked(object sender, EventArgs args)
    {
        if (_saving || !_viewModel.CanConfirm) return;
        _saving = true;
        Content.IsEnabled = false;
        SelectionError.IsVisible = false;
        try
        {
            await _service.SetTorrentFileSelectionAsync(_torrent, _viewModel.SelectedPaths);
            await CloseAsync(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving torrent file choices failed: {ex}");
            SelectionError.Text = LocalizationResourceManager.Instance["SaveTorrentFilesFailed"];
            SelectionError.IsVisible = true;
        }
        finally
        {
            _saving = false;
            Content.IsEnabled = true;
        }
    }

    private async void OnCancelClicked(object sender, EventArgs args) => await CloseAsync(false);

    private async Task CloseAsync(bool accepted)
    {
        if (_closing) return;
        _closing = true;
        _loading?.Cancel();
        _accepted |= accepted;
        try
        {
            if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"File picker dismissal failed: {ex}"); }
        finally
        {
            // A saved selection remains accepted even if navigation fails or Cancel is retried.
            _result.TrySetResult(_accepted);
            _closing = false;
        }
    }

    protected override bool OnBackButtonPressed() => _saving || base.OnBackButtonPressed();

    protected override void OnDisappearing()
    {
        _loading?.Cancel();
        FinishPresentation();
        base.OnDisappearing();
        // Window deactivation is not a cancellation of the user's file choices.
        Dispatcher.Dispatch(() =>
        {
            if (!Navigation.ModalStack.Contains(this)) _result.TrySetResult(_accepted);
        });
    }
}
