using TorrentFree.Models;

namespace TorrentFree.Services;

public static class AppRatingPromptRules
{
    public const int SuccessfulDownloadsThreshold = 3;
    public static readonly TimeSpan DeclineSnoozeDuration = TimeSpan.FromDays(30);

    public static void RecordSuccessfulDownload(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.HasAcceptedRatingPrompt)
        {
            return;
        }

        settings.SuccessfulDownloadsForRatingPrompt++;
    }

    public static bool ShouldPrompt(AppSettings settings, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.HasAcceptedRatingPrompt)
        {
            return false;
        }

        if (settings.SuccessfulDownloadsForRatingPrompt < SuccessfulDownloadsThreshold)
        {
            return false;
        }

        return settings.LastRatingPromptDeclinedUtc is not { } declinedAt
            || utcNow - declinedAt >= DeclineSnoozeDuration;
    }

    public static void RecordPromptAccepted(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.HasAcceptedRatingPrompt = true;
        settings.LastRatingPromptDeclinedUtc = null;
    }

    public static void RecordPromptDeclined(AppSettings settings, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.LastRatingPromptDeclinedUtc = utcNow;
    }
}
