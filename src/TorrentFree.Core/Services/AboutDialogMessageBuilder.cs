namespace TorrentFree.Services;

internal static class AboutDialogMessageBuilder
{
    public static string Build(
        string introMessage,
        string versionLabel,
        string? version,
        string buildLabel,
        string? build,
        string fileVersionLabel,
        string? fileVersion,
        string sourceLabel,
        string sourceUrl,
        string unavailableValue)
    {
        ArgumentNullException.ThrowIfNull(introMessage);
        ArgumentNullException.ThrowIfNull(versionLabel);
        ArgumentNullException.ThrowIfNull(buildLabel);
        ArgumentNullException.ThrowIfNull(fileVersionLabel);
        ArgumentNullException.ThrowIfNull(sourceLabel);
        ArgumentNullException.ThrowIfNull(sourceUrl);
        ArgumentNullException.ThrowIfNull(unavailableValue);

        var lines = new List<string>();
        var trimmedIntro = introMessage.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedIntro))
        {
            lines.Add(trimmedIntro);
            lines.Add(string.Empty);
        }

        lines.Add($"{versionLabel}: {Normalize(version, unavailableValue)}");
        lines.Add($"{buildLabel}: {Normalize(build, unavailableValue)}");
        lines.Add($"{fileVersionLabel}: {Normalize(fileVersion, unavailableValue)}");

        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            lines.Add(string.Empty);
            lines.Add($"{sourceLabel}: {sourceUrl.Trim()}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Normalize(string? value, string unavailableValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? unavailableValue
            : value.Trim();
    }
}
