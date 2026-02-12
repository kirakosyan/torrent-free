using Microsoft.Maui.ApplicationModel;
using TorrentFree.ViewModels;

namespace TorrentFree;

public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is MainViewModel vm)
        {
            await vm.InitializeCommand.ExecuteAsync(null);
        }
    }

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        var openSources = await DisplayAlertAsync(
            "About Torrent Free",
            "Torrent Free is free to use.\n\nSources: https://github.com/kirakosyan/torrent-free",
            "Open Source",
            "OK");

        if (openSources)
        {
            await Browser.Default.OpenAsync(
                new Uri("https://github.com/kirakosyan/torrent-free"),
                BrowserLaunchMode.External);
        }
    }
}
