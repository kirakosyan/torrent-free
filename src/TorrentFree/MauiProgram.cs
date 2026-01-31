using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;
using TorrentFree.Services;
using TorrentFree.ViewModels;

namespace TorrentFree;

public static class MauiProgram
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
#if ANDROID || IOS || MACCATALYST
            .UseLocalNotification()
#endif
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register Services
        builder.Services.AddSingleton<IStorageService, StorageService>();
        builder.Services.AddSingleton<ITorrentService, TorrentService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<ITorrentFilePicker, MauiTorrentFilePicker>();
        builder.Services.AddSingleton<ITorrentFileParser, TorrentFileParser>();
        builder.Services.AddSingleton<IFileAssociationService, FileAssociationService>();
        builder.Services.AddSingleton<TorrentFree.Services.INotificationService, NotificationService>();
    #if ANDROID
        builder.Services.AddSingleton<IBackgroundDownloadService, AndroidBackgroundDownloadService>();
    #else
        builder.Services.AddSingleton<IBackgroundDownloadService, BackgroundDownloadService>();
    #endif

        // Register ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // Register Pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        Services = app.Services;
        return app;
    }
}
