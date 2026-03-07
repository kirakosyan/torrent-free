using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace TorrentFree.Services;

/// <summary>
/// Singleton that wraps the resource manager and implements <see cref="INotifyPropertyChanged"/>
/// so XAML bindings via the indexer auto-refresh when the culture changes.
/// </summary>
public sealed class LocalizationResourceManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationResourceManager> _lazy = new(() => new LocalizationResourceManager());

    public static LocalizationResourceManager Instance => _lazy.Value;

    /// <summary>
    /// The system UI culture captured at app startup, before any language override is applied.
    /// Use this to revert to the user's original preferred language ("System Default").
    /// </summary>
    public static readonly CultureInfo OriginalSystemCulture = CultureInfo.CurrentUICulture;

    private readonly ResourceManager _resourceManager;
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    private LocalizationResourceManager()
    {
        _resourceManager = new ResourceManager(
            "TorrentFree.Resources.Strings.AppResources",
            typeof(LocalizationResourceManager).Assembly);
    }

    /// <summary>
    /// Indexer used by XAML bindings: <c>{Binding [Key], Source={x:Static ...}}</c>.
    /// </summary>
    public string this[string key] =>
        _resourceManager.GetString(key, _culture) ?? key;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets the active culture and raises <see cref="PropertyChanged"/> so every binding re-evaluates.
    /// </summary>
    public void SetCulture(CultureInfo culture)
    {
        _culture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
