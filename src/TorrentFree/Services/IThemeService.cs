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
