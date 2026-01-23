using TorrentFree.ViewModels;

namespace TorrentFree;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is SettingsViewModel vm)
        {
            await vm.InitializeCommand.ExecuteAsync(null);
        }
    }
}