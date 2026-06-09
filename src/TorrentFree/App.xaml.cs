using System.Globalization;
using Microsoft.Maui.Storage;
using TorrentFree.Models;
using TorrentFree.Services;

namespace TorrentFree;

public partial class App : Application
{
    private static readonly object CrashLogLock = new();
    private static readonly string CrashLogPath = GetCrashLogPath();

    private readonly AppShell _appShell;
    private readonly IStorageService _storageService;
    private readonly ILocalizationService _localizationService;
    private readonly IThemeService _themeService;

    public App(AppShell appShell, IStorageService storageService, ILocalizationService localizationService, IThemeService themeService)
    {
        InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception, e.ExceptionObject);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception, e.Exception);
            e.SetObserved();
        };

        _appShell = appShell;
        _storageService = storageService;
        _localizationService = localizationService;
        _themeService = themeService;
    }

    private static string GetCrashLogPath()
    {
        var appDataDirectory = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TorrentFree")
            : FileSystem.AppDataDirectory;

        return Path.Combine(appDataDirectory, "crash.log");
    }

    private static void LogCrash(string source, Exception? ex, object? rawValue)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            var details = ex?.ToString() ?? rawValue?.ToString() ?? "(no exception information)";
            var entry = $"""
                [{DateTime.UtcNow:O}] {source}
                {details}
                ---

                """;
            lock (CrashLogLock)
            {
                File.AppendAllText(CrashLogPath, entry);
            }
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
