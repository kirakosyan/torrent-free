using System.Globalization;
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
        UpdateBackButtonGlyph();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateBackButtonGlyph();

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
        var separatorSeen = false;
        var decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        foreach (var ch in text)
        {
            if (char.IsDigit(ch))
            {
                result.Append(ch);
            }
            else if (!separatorSeen && IsDecimalSeparator(ch))
            {
                separatorSeen = true;
                result.Append(decimalSeparator);
            }
        }

        var filtered = result.ToString();
        if (text != filtered)
        {
            entry.Text = filtered;
        }
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(FlowDirection))
        {
            UpdateBackButtonGlyph();
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopAsync();
        }
    }

    private void UpdateBackButtonGlyph()
    {
        if (BackButton is null)
        {
            return;
        }

        BackButton.Text = FlowDirection == FlowDirection.RightToLeft ? "→" : "←";
    }

    private static bool IsDecimalSeparator(char ch) => ch is '.' or ',' or '\u066B';

    private static T GetRequiredService<T>() where T : notnull
    {
        return MauiProgram.Services.GetRequiredService<T>();
    }
}
