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
        if (!_loaded) await LoadFilesAsync();
    }

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
            LoadingIndicator.IsRunning = false;
            if (ReferenceEquals(_loading, loading)) _loading = null;
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
        _loading?.Cancel();
        _accepted = accepted;
        try
        {
            if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync();
            _result.TrySetResult(accepted);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"File picker dismissal failed: {ex}"); }
    }

    protected override bool OnBackButtonPressed() => _saving || base.OnBackButtonPressed();

    protected override void OnDisappearing()
    {
        _loading?.Cancel();
        base.OnDisappearing();
        // Window deactivation is not a cancellation of the user's file choices.
        Dispatcher.Dispatch(() =>
        {
            if (!Navigation.ModalStack.Contains(this)) _result.TrySetResult(_accepted);
        });
    }
}
