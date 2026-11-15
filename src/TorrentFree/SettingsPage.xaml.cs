using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using TorrentFree.ViewModels;
using TorrentFree.Services;

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
                vm.PropertyChanged -= OnSettingsPropertyChanged;
                vm.PropertyChanged += OnSettingsPropertyChanged;
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
        if (BindingContext is SettingsViewModel vm) vm.PropertyChanged -= OnSettingsPropertyChanged;
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
            vm.ValidationMessage = LocalizationResourceManager.Instance["ErrorSaveSettings"];
            AppDiagnostics.Record("Settings.Flush", ex);
            System.Diagnostics.Debug.WriteLine($"Settings flush failed: {ex}");
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ValidationMessage) &&
            BindingContext is SettingsViewModel { ValidationMessage: { Length: > 0 } message })
            SemanticScreenReader.Default.Announce(message);
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
            if (BindingContext is SettingsViewModel vm) vm.ValidationMessage = LocalizationResourceManager.Instance["ErrorSaveSettings"];
            AppDiagnostics.Record("Settings.Flush", ex);
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

    private static T GetRequiredService<T>() where T : notnull
    {
        return MauiProgram.Services.GetRequiredService<T>();
    }
}
