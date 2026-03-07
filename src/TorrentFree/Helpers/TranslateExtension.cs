namespace TorrentFree.Helpers;

/// <summary>
/// XAML markup extension that produces a binding to a localization resource key.
/// Usage: <c>Text="{local:Translate BrowseButton}"</c>.
/// The binding auto-refreshes when <see cref="Services.LocalizationResourceManager.SetCulture"/> is called.
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class TranslateExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = string.Empty;

    public string? StringFormat { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding
        {
            Mode = BindingMode.OneWay,
            Path = $"[{Key}]",
            Source = Services.LocalizationResourceManager.Instance
        };

        if (!string.IsNullOrEmpty(StringFormat))
            binding.StringFormat = StringFormat;

        return binding;
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
        ProvideValue(serviceProvider);
}
