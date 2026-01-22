using System.Globalization;
using System.Resources;

namespace TorrentFree.Services;

/// <summary>
/// Interface for localization service.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets a localized string by key.
    /// </summary>
    string GetString(string key);

    /// <summary>
    /// Gets the current culture.
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Sets the current culture.
    /// </summary>
    void SetCulture(CultureInfo culture);
}

/// <summary>
/// Service for handling application localization.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    public LocalizationService()
    {
        _resourceManager = new ResourceManager(
            "TorrentFree.Resources.Strings.AppResources",
            typeof(LocalizationService).Assembly);
        _currentCulture = CultureInfo.CurrentUICulture;
    }

    /// <inheritdoc />
    public CultureInfo CurrentCulture => _currentCulture;

    /// <inheritdoc />
    public string GetString(string key)
    {
        try
        {
            return _resourceManager.GetString(key, _currentCulture) ?? key;
        }
        catch (Exception ex)
        {
            // Log the error for debugging - helps identify missing or corrupted resources
            System.Diagnostics.Debug.WriteLine($"Localization error for key '{key}': {ex.Message}");
            return key;
        }
    }

    /// <inheritdoc />
    public void SetCulture(CultureInfo culture)
    {
        _currentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }
}
