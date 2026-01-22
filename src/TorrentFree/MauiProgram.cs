using Microsoft.Extensions.Logging;
using TorrentFree.Services;
using TorrentFree.ViewModels;

namespace TorrentFree;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register Services
        builder.Services.AddSingleton<IStorageService, StorageService>();
        builder.Services.AddSingleton<ITorrentService, TorrentService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

        // Register ViewModels
        builder.Services.AddSingleton<MainViewModel>();

        // Register Pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
