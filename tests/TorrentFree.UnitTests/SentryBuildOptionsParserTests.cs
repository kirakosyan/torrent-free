using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class SentryBuildOptionsParserTests
{
    [Fact]
    public void Parse_UsesMetadataValues_WhenPresent()
    {
        var metadata = new Dictionary<string, string?>
        {
            ["SentryDsn"] = " https://public@example.ingest.sentry.io/123 ",
            ["SentryEnvironment"] = "staging",
            ["SentryTracesSampleRate"] = "0.25"
        };

        var options = SentryBuildOptionsParser.Parse(
            metadata,
            packageName: "com.torrentfree.app",
            displayVersion: "17.0",
            buildNumber: "17",
            environmentDsn: null,
            environmentName: null,
            isDebug: false);

        Assert.True(options.IsEnabled);
        Assert.Equal("https://public@example.ingest.sentry.io/123", options.Dsn);
        Assert.Equal("staging", options.Environment);
        Assert.Equal(0.25, options.TracesSampleRate, 3);
        Assert.Equal("com.torrentfree.app@17.0+17", options.Release);
        Assert.Equal("17", options.Distribution);
    }

    [Fact]
    public void Parse_FallsBackToEnvironmentVariables_WhenMetadataMissing()
    {
        var options = SentryBuildOptionsParser.Parse(
            new Dictionary<string, string?>(),
            packageName: "com.torrentfree.app",
            displayVersion: "17.0",
            buildNumber: "17",
            environmentDsn: "https://env@example.ingest.sentry.io/456",
            environmentName: "production",
            isDebug: false);

        Assert.True(options.IsEnabled);
        Assert.Equal("https://env@example.ingest.sentry.io/456", options.Dsn);
        Assert.Equal("production", options.Environment);
        Assert.Equal(0.1, options.TracesSampleRate, 3);
    }

    [Fact]
    public void Parse_UsesDebugDefaults_WhenOptionalValuesAreMissing()
    {
        var options = SentryBuildOptionsParser.Parse(
            new Dictionary<string, string?>(),
            packageName: "com.torrentfree.app",
            displayVersion: "17.0",
            buildNumber: "17",
            environmentDsn: null,
            environmentName: null,
            isDebug: true);

        Assert.False(options.IsEnabled);
        Assert.Equal("development", options.Environment);
        Assert.Equal(1.0, options.TracesSampleRate, 3);
    }

    [Fact]
    public void Parse_IgnoresInvalidTraceSampleRate()
    {
        var metadata = new Dictionary<string, string?>
        {
            ["SentryTracesSampleRate"] = "not-a-number"
        };

        var options = SentryBuildOptionsParser.Parse(
            metadata,
            packageName: "com.torrentfree.app",
            displayVersion: "17.0",
            buildNumber: "17",
            environmentDsn: null,
            environmentName: null,
            isDebug: false);

        Assert.Equal(0.1, options.TracesSampleRate, 3);
    }
}
