using System.Globalization;
using Microsoft.Maui.Storage;
using TorrentFree.Models;
using TorrentFree.Services;

namespace TorrentFree;

public partial class App : Application
{
    private readonly AppShell _appShell;
    private readonly IStorageService _storageService;
    private readonly ILocalizationService _localizationService;
    private readonly IThemeService _themeService;
    private readonly ITorrentService _torrentService;

    public App(AppShell appShell, IStorageService storageService, ILocalizationService localizationService, IThemeService themeService, ITorrentService torrentService)
    {
        AppDiagnostics.Initialize();
        InitializeComponent();

        _appShell = appShell;
        _storageService = storageService;
        _localizationService = localizationService;
        _themeService = themeService;
        _torrentService = torrentService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_appShell);
        window.Page!.FlowDirection = _localizationService.CurrentCulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        window.Created += OnWindowCreated;
        ConfigurePlatformWindow(window);
        return window;
    }

    private async void OnWindowCreated(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Created -= OnWindowCreated;

        AppSettings settings;
        try
        {
            settings = await _storageService.LoadSettingsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load saved settings: {ex.Message}");
            return;
        }

        ApplyPlatformWindowSettings(window, settings);
        _themeService.Apply(settings.Theme);

        if (string.IsNullOrEmpty(settings.Language))
        {
            return;
        }

        try
        {
            _localizationService.SetCulture(new CultureInfo(settings.Language));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply saved language: {ex.Message}");
        }
    }

    partial void ConfigurePlatformWindow(Window window);

    partial void ApplyPlatformWindowSettings(Window window, AppSettings settings);
}
