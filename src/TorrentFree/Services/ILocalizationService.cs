using System.Globalization;

namespace TorrentFree.Services;

/// <summary>
/// Interface for localization service.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets the current culture.
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Sets the current culture.
    /// </summary>
    void SetCulture(CultureInfo culture);
}
