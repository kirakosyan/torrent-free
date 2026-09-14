using CommunityToolkit.Mvvm.ComponentModel;
using TorrentFree.Models;

namespace TorrentFree;

public partial class DeleteTorrentDialogPage : ContentPage
{
    private readonly TaskCompletionSource<DeleteTorrentDialogResult?> _tcs = new();

    public DeleteTorrentDialogPage(string torrentName)
    {
        InitializeComponent();
        ViewModel = new DeleteTorrentDialogViewModel(torrentName);
        BindingContext = ViewModel;
    }

    public Task<DeleteTorrentDialogResult?> Result => _tcs.Task;

    public DeleteTorrentDialogViewModel ViewModel { get; }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // If the modal was dismissed via system gestures/back, complete the awaiting task.
        Dispatcher.Dispatch(() =>
        {
            if (!Navigation.ModalStack.Contains(this))
            {
                _tcs.TrySetResult(null);
            }
        });
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        try
        {
            if (Navigation.ModalStack.Contains(this))
            {
                await Navigation.PopModalAsync();
            }
        }
        catch (Exception ex)
        {
            // async void: an unhandled exception here would crash the app. The result is
            // already delivered via _tcs above, so a failed pop is safe to just log.
            System.Diagnostics.Debug.WriteLine($"Delete dialog cancel dismiss error: {ex}");
        }
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        var result = new DeleteTorrentDialogResult(ViewModel.DeleteTorrentFile, ViewModel.DeleteDownloadedFiles);
        _tcs.TrySetResult(result);
        try
        {
            if (Navigation.ModalStack.Contains(this))
            {
                await Navigation.PopModalAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delete dialog dismiss error: {ex}");
        }
    }
}

public partial class DeleteTorrentDialogViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string TorrentName { get; set; }

    [ObservableProperty]
    public partial bool DeleteTorrentFile { get; set; }

    [ObservableProperty]
    public partial bool DeleteDownloadedFiles { get; set; }

    public DeleteTorrentDialogViewModel(string torrentName)
    {
        TorrentName = torrentName;
    }

    /// <summary>
    /// Always allow the delete/remove action. When both checkboxes are unchecked
    /// the torrent is removed from the list without deleting any files.
    /// </summary>
    public bool CanDelete => true;
}
