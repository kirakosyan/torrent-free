using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AppRatingPromptRulesTests
{
    [Fact]
    public void ShouldPrompt_AfterThirdSuccessfulDownload()
    {
        var settings = new AppSettings();
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);

        AppRatingPromptRules.RecordSuccessfulDownload(settings);
        AppRatingPromptRules.RecordSuccessfulDownload(settings);

        Assert.False(AppRatingPromptRules.ShouldPrompt(settings, now));

        AppRatingPromptRules.RecordSuccessfulDownload(settings);

        Assert.True(AppRatingPromptRules.ShouldPrompt(settings, now));
    }

    [Fact]
    public void ShouldPrompt_WaitsOneMonthAfterDecline()
    {
        var declinedAt = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings
        {
            SuccessfulDownloadsForRatingPrompt = 3,
            LastRatingPromptDeclinedUtc = declinedAt
        };

        Assert.False(AppRatingPromptRules.ShouldPrompt(settings, declinedAt.AddDays(29)));
        Assert.True(AppRatingPromptRules.ShouldPrompt(settings, declinedAt.AddDays(30)));
    }

    [Fact]
    public void ShouldPrompt_StopsAfterAccepted()
    {
        var settings = new AppSettings
        {
            SuccessfulDownloadsForRatingPrompt = 3
        };

        AppRatingPromptRules.RecordPromptAccepted(settings);
        AppRatingPromptRules.RecordSuccessfulDownload(settings);

        Assert.True(settings.HasAcceptedRatingPrompt);
        Assert.Equal(3, settings.SuccessfulDownloadsForRatingPrompt);
        Assert.False(AppRatingPromptRules.ShouldPrompt(settings, DateTime.UtcNow));
    }
}
