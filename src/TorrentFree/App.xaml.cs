using System.Globalization;
using TorrentFree.Services;

namespace TorrentFree;

public partial class App : Application
{
    private readonly AppShell _appShell;
    private readonly IStorageService _storageService;
    private readonly ILocalizationService _localizationService;

    public App(AppShell appShell, IStorageService storageService, ILocalizationService localizationService)
    {
        InitializeComponent();
        _appShell = appShell;
        _storageService = storageService;
        _localizationService = localizationService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_appShell);
        window.Page!.FlowDirection = _localizationService.CurrentCulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        window.Created += OnWindowCreated;
        return window;
    }

    private async void OnWindowCreated(object? sender, EventArgs e)
    {
        if (sender is Window w)
            w.Created -= OnWindowCreated;

        try
        {
            var settings = await _storageService.LoadSettingsAsync();
            if (!string.IsNullOrEmpty(settings.Language))
            {
                _localizationService.SetCulture(new CultureInfo(settings.Language));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply saved language: {ex.Message}");
        }
    }
}
