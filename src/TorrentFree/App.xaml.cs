using System.Globalization;
using TorrentFree.Services;

namespace TorrentFree;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TorrentFree",
        "crash.log");

    private readonly AppShell _appShell;
    private readonly IStorageService _storageService;
    private readonly ILocalizationService _localizationService;

    public App(AppShell appShell, IStorageService storageService, ILocalizationService localizationService)
    {
        InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        _appShell = appShell;
        _storageService = storageService;
        _localizationService = localizationService;
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            var entry = $"""
                [{DateTime.UtcNow:O}] {source}
                {ex}
                ---

                """;
            File.AppendAllText(CrashLogPath, entry);
        }
        catch
        {
            // Nothing we can do if logging itself fails.
        }
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
