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

/// <summary>
/// Service for handling application localization.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private CultureInfo _currentCulture;

    public LocalizationService()
    {
        _currentCulture = LocalizationResourceManager.OriginalSystemCulture;
    }

    /// <inheritdoc />
    public CultureInfo CurrentCulture => _currentCulture;

    /// <inheritdoc />
    public void SetCulture(CultureInfo culture)
    {
        _currentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        LocalizationResourceManager.Instance.SetCulture(culture);
        ApplyFlowDirection(culture);
    }

    private static void ApplyFlowDirection(CultureInfo culture)
    {
        if (Application.Current is null)
        {
            return;
        }

        var flowDirection = culture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        foreach (var window in Application.Current.Windows)
        {
            if (window.Page is not null)
            {
                window.Page.FlowDirection = flowDirection;
            }
        }
    }
}
