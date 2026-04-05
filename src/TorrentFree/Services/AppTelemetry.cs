using System.Diagnostics;
using System.Reflection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Sentry;
using Sentry.Maui;

namespace TorrentFree.Services;

internal static class AppTelemetry
{
    private static readonly Lazy<SentryBuildOptions> CurrentOptions = new(LoadOptions);

    public static SentryBuildOptions Options => CurrentOptions.Value;

    public static void Configure(SentryMauiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var buildOptions = Options;
        options.Dsn = buildOptions.Dsn;
        options.Environment = buildOptions.Environment;
        options.Release = buildOptions.Release;
        options.Distribution = buildOptions.Distribution;
        options.SampleRate = 1.0f;
        options.TracesSampleRate = buildOptions.TracesSampleRate;
        options.EnableLogs = true;
        options.SendDefaultPii = false;
        options.IsEnvironmentUser = false;
        options.MaxBreadcrumbs = 50;
        options.CacheDirectoryPath = Path.Combine(FileSystem.CacheDirectory, "Sentry");
        options.AddInAppInclude("TorrentFree");
        options.AddExceptionFilterForType<OperationCanceledException>();
        options.AddCommunityToolkitIntegration();
#if DEBUG
        options.Debug = true;
#endif
    }

    public static void CaptureHandledException(Exception exception, string context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException || !Options.IsEnabled)
        {
            return;
        }

        SentrySdk.AddBreadcrumb(
            message: context,
            category: "handled-exception",
            level: BreadcrumbLevel.Error);

        SentrySdk.CaptureException(exception);
    }

    private static SentryBuildOptions LoadOptions()
    {
        var assembly = typeof(MauiProgram).Assembly;
        var metadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .GroupBy(static attribute => attribute.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Value,
                StringComparer.Ordinal);

        var packageName = GetPackageName(assembly);
        var displayVersion = GetMetadataValue(metadata, "DisplayVersion") ?? GetAppVersion();
        var buildNumber = GetMetadataValue(metadata, "BuildNumber") ?? GetAppBuildNumber();

        return SentryBuildOptionsParser.Parse(
            metadata,
            packageName: packageName,
            displayVersion: displayVersion,
            buildNumber: buildNumber,
            environmentDsn: Environment.GetEnvironmentVariable("SENTRY_DSN"),
            environmentName: Environment.GetEnvironmentVariable("SENTRY_ENVIRONMENT"),
            isDebug: Debugger.IsAttached);
    }

    private static string? GetMetadataValue(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static string GetPackageName(Assembly assembly)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AppInfo.Current.PackageName))
            {
                return AppInfo.Current.PackageName;
            }
        }
        catch
        {
        }

        return assembly.GetName().Name ?? "TorrentFree";
    }

    private static string GetAppVersion()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AppInfo.Current.VersionString))
            {
                return AppInfo.Current.VersionString;
            }
        }
        catch
        {
        }

        return "0.0";
    }

    private static string GetAppBuildNumber()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AppInfo.Current.BuildString))
            {
                return AppInfo.Current.BuildString;
            }
        }
        catch
        {
        }

        return "0";
    }
}
