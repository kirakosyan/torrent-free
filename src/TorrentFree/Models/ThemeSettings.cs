namespace TorrentFree.Models;

/// <summary>
/// Canonical theme preference codes and normalization, kept free of any MAUI types
/// so the rule can be unit tested in isolation. The string→<c>AppTheme</c> mapping
/// lives in the app layer (see <c>ThemeService</c>).
/// </summary>
public static class ThemeSettings
{
    /// <summary>Follow the operating-system theme.</summary>
    public const string System = "system";

    /// <summary>Force the light theme.</summary>
    public const string Light = "light";

    /// <summary>Force the dark theme.</summary>
    public const string Dark = "dark";

    /// <summary>
    /// Maps any stored or user-supplied value to a known theme code, defaulting to
    /// <see cref="System"/> for null/blank/unrecognized input.
    /// </summary>
    public static string Normalize(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Light => Light,
        Dark => Dark,
        _ => System,
    };
}
