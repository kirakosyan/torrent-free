using System.Text.RegularExpressions;

namespace TorrentFree.Services;

internal static partial class MagnetTrackerBootstrapRules
{
    public static bool ShouldAddPublicTrackers(string magnetLink)
    {
        if (string.IsNullOrWhiteSpace(magnetLink))
        {
            return false;
        }

        return !TrackerParameterRegex().IsMatch(magnetLink);
    }

    [GeneratedRegex(@"(?:^|[?&])tr=", RegexOptions.IgnoreCase)]
    private static partial Regex TrackerParameterRegex();
}
