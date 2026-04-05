namespace TorrentFree.Services;

internal sealed record SentryBuildOptions(
    string? Dsn,
    string Environment,
    double TracesSampleRate,
    string Release,
    string Distribution)
{
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Dsn);
}

internal static class SentryBuildOptionsParser
{
    public static SentryBuildOptions Parse(
        IReadOnlyDictionary<string, string?> metadata,
        string packageName,
        string displayVersion,
        string buildNumber,
        string? environmentDsn,
        string? environmentName,
        bool isDebug)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        packageName = FirstNonEmpty(packageName, "TorrentFree");
        displayVersion = FirstNonEmpty(displayVersion, "0.0");
        buildNumber = FirstNonEmpty(buildNumber, "0");

        var dsn = Normalize(FirstNonEmpty(
            Get(metadata, "SentryDsn"),
            environmentDsn));

        var environment = FirstNonEmpty(
            Get(metadata, "SentryEnvironment"),
            environmentName,
            isDebug ? "development" : "production");

        var tracesSampleRate = ParseSampleRate(
            Get(metadata, "SentryTracesSampleRate"),
            defaultValue: isDebug ? 1.0 : 0.1);

        return new SentryBuildOptions(
            dsn,
            environment,
            tracesSampleRate,
            $"{packageName}@{displayVersion}+{buildNumber}",
            buildNumber);
    }

    private static string? Get(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value)
            ? Normalize(value)
            : null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (normalized is not null)
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    private static double ParseSampleRate(string? value, double defaultValue)
    {
        if (Normalize(value) is null)
        {
            return defaultValue;
        }

        if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return defaultValue;
        }

        return parsed is >= 0 and <= 1
            ? parsed
            : defaultValue;
    }
}
