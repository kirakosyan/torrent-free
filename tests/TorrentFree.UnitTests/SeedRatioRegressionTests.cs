using TorrentFree.Models;
using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class SeedRatioRegressionTests
{
    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(-1, 0)]
    [InlineData(101, 100)]
    [InlineData(1.5, 1.5)]
    public async Task InvalidRatio_CannotBreakTorrentOrSettingsPersistence(double input, double expected)
    {
        using var directory = new CoreTestDirectory();
        using var storage = new StorageService(directory.StoragePaths);
        await storage.LoadTorrentsAsync();
        var first = new TorrentItem { Name = "Edited", MaxSeedRatio = input };
        var second = new TorrentItem { Name = "Other" };
        var settings = new AppSettings { GlobalMaxSeedRatio = input };
        await storage.SaveTorrentsAsync([first, second]);
        await storage.SaveSettingsAsync(settings);
        var restored = await storage.LoadTorrentsAsync();
        Assert.Equal(2, restored.Count);
        Assert.Equal(expected, restored.Single(t => t.Id == first.Id).MaxSeedRatio);
        Assert.Equal(expected, (await storage.LoadSettingsAsync()).GlobalMaxSeedRatio);
        Assert.True(double.IsFinite(first.MaxSeedRatio));
    }
}
