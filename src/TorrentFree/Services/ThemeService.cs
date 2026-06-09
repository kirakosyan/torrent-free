using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Applies the user's theme preference to the running application.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Applies the given theme code ("system", "light", or "dark") to the app.
    /// Unknown values fall back to following the system theme.
    /// </summary>
    void Apply(string? themeCode);
}

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
