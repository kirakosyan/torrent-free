using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Maps a stored theme code onto <see cref="Application.UserAppTheme"/>.
/// </summary>
public sealed class ThemeService : IThemeService
{
    /// <inheritdoc />
    public void Apply(string? themeCode)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.UserAppTheme = Map(themeCode);
    }

    private static AppTheme Map(string? themeCode) => ThemeSettings.Normalize(themeCode) switch
    {
        ThemeSettings.Light => AppTheme.Light,
        ThemeSettings.Dark => AppTheme.Dark,
        _ => AppTheme.Unspecified,
    };
}
