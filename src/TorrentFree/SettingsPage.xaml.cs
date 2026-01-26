using Microsoft.Extensions.DependencyInjection;
using TorrentFree.ViewModels;

namespace TorrentFree;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
        : this(GetRequiredService<SettingsViewModel>())
    {
    }

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

    private void OnNumericTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not Entry entry)
        {
            return;
        }

        var text = entry.Text ?? string.Empty;
        var filtered = new string(text.Where(char.IsDigit).ToArray());

        if (text != filtered)
        {
            entry.Text = filtered;
        }
    }

    private void OnDecimalTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not Entry entry)
        {
            return;
        }

        var text = entry.Text ?? string.Empty;
        var result = new System.Text.StringBuilder();
        var dotSeen = false;

        foreach (var ch in text)
        {
            if (char.IsDigit(ch))
            {
                result.Append(ch);
            }
            else if (ch == '.' && !dotSeen)
            {
                dotSeen = true;
                result.Append(ch);
            }
        }

        var filtered = result.ToString();
        if (text != filtered)
        {
            entry.Text = filtered;
        }
    }

    private static T GetRequiredService<T>() where T : notnull
    {
        return MauiProgram.Services.GetRequiredService<T>();
    }
}