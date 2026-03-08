using System.Reflection;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using TorrentFree.Services;
using TorrentFree.ViewModels;

namespace TorrentFree;

public partial class MainPage : ContentPage
{
    private const string GitHubUrl = "https://github.com/kirakosyan/torrent-free";

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is MainViewModel vm)
        {
            await vm.InitializeCommand.ExecuteAsync(null);
        }
    }

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        var aboutMessage = AboutDialogMessageBuilder.Build(
            LocalizationResourceManager.Instance["AboutMessage"],
            LocalizationResourceManager.Instance["AboutVersionLabel"],
            TryGetAppVersion(),
            LocalizationResourceManager.Instance["AboutBuildLabel"],
            TryGetBuildNumber(),
            LocalizationResourceManager.Instance["AboutFileVersionLabel"],
            TryGetFileVersion(),
            LocalizationResourceManager.Instance["AboutSourceLabel"],
            GitHubUrl,
            LocalizationResourceManager.Instance["AboutUnavailable"]);

        var openSources = await DisplayAlertAsync(
            LocalizationResourceManager.Instance["AboutTitle"],
            aboutMessage,
            LocalizationResourceManager.Instance["OpenSource"],
            LocalizationResourceManager.Instance["OK"]);

        if (openSources)
        {
            await Browser.Default.OpenAsync(
                new Uri(GitHubUrl),
                BrowserLaunchMode.External);
        }
    }

    private static string? TryGetAppVersion()
    {
        try
        {
            var assembly = GetAppAssembly();
            return GetAssemblyMetadata(assembly, "DisplayVersion")
                ?? assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
                ?? AppInfo.Current.VersionString;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetBuildNumber()
    {
        try
        {
            var assembly = GetAppAssembly();
            return GetAssemblyMetadata(assembly, "BuildNumber")
                ?? AppInfo.Current.BuildString;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetFileVersion()
    {
        try
        {
            var assembly = GetAppAssembly();
            return assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? assembly.GetName().Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static Assembly GetAppAssembly() => Assembly.GetEntryAssembly() ?? typeof(App).Assembly;

    private static string? GetAssemblyMetadata(Assembly assembly, string key)
    {
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;
    }
}
