using Microsoft.Maui.ApplicationModel;
using TorrentFree.Services;
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
            LocalizationResourceManager.Instance["AboutTitle"],
            LocalizationResourceManager.Instance["AboutMessage"],
            LocalizationResourceManager.Instance["OpenSource"],
            LocalizationResourceManager.Instance["OK"]);

        if (openSources)
        {
            await Browser.Default.OpenAsync(
                new Uri("https://github.com/kirakosyan/torrent-free"),
                BrowserLaunchMode.External);
        }
    }

}
