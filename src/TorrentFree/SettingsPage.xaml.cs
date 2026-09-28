using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using TorrentFree.ViewModels;

namespace TorrentFree;

public partial class SettingsPage : ContentPage
{
    private Window? _settingsWindow;

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
            try
            {
                if (_settingsWindow is null && Window is { } window)
                {
                    _settingsWindow = window;
                    window.Stopped += OnWindowStopped;
                    window.Destroying += OnWindowStopped;
                }
                await vm.InitializeCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                // async void: an unhandled exception here would crash the app.
                System.Diagnostics.Debug.WriteLine($"Settings page appearing error: {ex}");
            }
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (_settingsWindow is { } window)
        {
            window.Stopped -= OnWindowStopped;
            window.Destroying -= OnWindowStopped;
            _settingsWindow = null;
        }
        await FlushPendingSettingsAsync();
    }

    private async void OnWindowStopped(object? sender, EventArgs e) => await FlushPendingSettingsAsync();

    private async Task FlushPendingSettingsAsync()
    {
        if (BindingContext is not SettingsViewModel vm) return;
        try { await vm.FlushPendingSettingsAsync(); }
        catch (Exception ex)
        {
            vm.ValidationMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"Settings flush failed: {ex}");
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
        try
        {
            if (BindingContext is SettingsViewModel vm) await vm.FlushPendingSettingsAsync();
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
        catch (Exception ex)
        {
            // async void: an unhandled exception here would crash the app.
            if (BindingContext is SettingsViewModel vm) vm.ValidationMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"Settings back navigation error: {ex}");
        }
    }

    private void UpdateBackButtonGlyph()
    {
        if (BackButton is null)
        {
            return;
        }

        BackButton.Text = GetEffectiveFlowDirection() == FlowDirection.RightToLeft ? "→" : "←";
    }

    private FlowDirection GetEffectiveFlowDirection()
    {
        if (FlowDirection != FlowDirection.MatchParent)
        {
            return FlowDirection;
        }

        Element? current = Parent;
        while (current is not null)
        {
            if (current is VisualElement visualElement &&
                visualElement.FlowDirection != FlowDirection.MatchParent)
            {
                return visualElement.FlowDirection;
            }

            current = current.Parent;
        }

        if (Window?.Page is VisualElement rootPage &&
            rootPage != this &&
            rootPage.FlowDirection != FlowDirection.MatchParent)
        {
            return rootPage.FlowDirection;
        }

        return CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    private static bool IsDecimalSeparator(char ch) => ch is '.' or ',' or '\u066B';

    private static T GetRequiredService<T>() where T : notnull
    {
        return MauiProgram.Services.GetRequiredService<T>();
    }
}
