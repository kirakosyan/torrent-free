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

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        if (Navigation.ModalStack.Contains(this))
        {
            await Navigation.PopModalAsync();
        }
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        var result = new DeleteTorrentDialogResult(ViewModel.DeleteTorrentFile, ViewModel.DeleteDownloadedFiles);
        _tcs.TrySetResult(result);
        if (Navigation.ModalStack.Contains(this))
        {
            await Navigation.PopModalAsync();
        }
    }
}

public partial class DeleteTorrentDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string torrentName;

    [ObservableProperty]
    private bool deleteTorrentFile;

    [ObservableProperty]
    private bool deleteDownloadedFiles;

    public DeleteTorrentDialogViewModel(string torrentName)
    {
        TorrentName = torrentName;
    }

    public bool CanDelete => DeleteTorrentFile || DeleteDownloadedFiles;

    partial void OnDeleteTorrentFileChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDelete));
    }

    partial void OnDeleteDownloadedFilesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDelete));
    }
}
