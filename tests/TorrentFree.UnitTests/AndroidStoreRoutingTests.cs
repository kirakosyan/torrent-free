using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class AndroidStoreRoutingTests
{
    [Theory]
    [InlineData("com.android.vending", "GooglePlay")]
    [InlineData("com.sec.android.app.samsungapps", "GalaxyStore")]
    [InlineData("com.huawei.appmarket", "HuaweiAppGallery")]
    [InlineData("com.huawei.appmarket.fake", "Unknown")]
    [InlineData(null, "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData("com.android.packageinstaller", "Unknown")]
    [InlineData("com.amazon.venezia", "Unknown")]
    [InlineData("com.android.vending.fake", "Unknown")]
    public void InstallerDeterminesStore(string? installer, string expected) =>
        Assert.Equal(expected, AndroidStoreRouting.FromInstaller(installer).ToString());

    [Fact]
    public void GalaxyActionsStayInGalaxyStore()
    {
        var review = AndroidStoreRouting.GetLink(AndroidStore.GalaxyStore, "com.torrentfree.app", review: true)!;
        Assert.Equal("com.sec.android.app.samsungapps", review.StorePackage);
        Assert.Equal("samsungapps://AppRating/com.torrentfree.app", review.AppUri);
        Assert.Equal("https://apps.samsung.com/appquery/AppRating.as?appId=com.torrentfree.app", review.WebUri);

        var update = AndroidStoreRouting.GetLink(AndroidStore.GalaxyStore, "com.torrentfree.app")!;
        Assert.Equal(review.StorePackage, update.StorePackage);
        Assert.Equal("samsungapps://ProductDetail/com.torrentfree.app", update.AppUri);
        Assert.Equal("https://galaxystore.samsung.com/detail/com.torrentfree.app", update.WebUri);
        Assert.False(AndroidStoreRouting.SupportsUpdateCheck(AndroidStore.GalaxyStore));
    }

    [Fact]
    public void HuaweiActionsStayInAppGalleryWithoutPlayUpdateChecks()
    {
        const string package = "com.torrentfree.app.huawei";
        var listing = AndroidStoreRouting.GetLink(AndroidStore.HuaweiAppGallery, package)!;
        Assert.Equal("com.huawei.appmarket", listing.StorePackage);
        Assert.Equal("appmarket://details?id=" + package, listing.AppUri);
        Assert.Null(listing.WebUri);
        Assert.Equal(listing, AndroidStoreRouting.GetLink(AndroidStore.HuaweiAppGallery, package, review: true));
        Assert.False(AndroidStoreRouting.SupportsUpdateCheck(AndroidStore.HuaweiAppGallery));
    }

    [Fact]
    public void GooglePlayKeepsItsUpdateApiAndListing()
    {
        var listing = AndroidStoreRouting.GetLink(AndroidStore.GooglePlay, "com.torrentfree.app")!;
        Assert.Equal("com.android.vending", listing.StorePackage);
        Assert.Equal("market://details?id=com.torrentfree.app", listing.AppUri);
        Assert.Equal("https://play.google.com/store/apps/details?id=com.torrentfree.app", listing.WebUri);
        Assert.Equal(listing, AndroidStoreRouting.GetLink(AndroidStore.GooglePlay, "com.torrentfree.app", review: true));
        Assert.True(AndroidStoreRouting.SupportsUpdateCheck(AndroidStore.GooglePlay));
    }

    [Fact]
    public void UnknownInstallersDoNotFallBackToGooglePlay()
    {
        Assert.Null(AndroidStoreRouting.GetLink(AndroidStore.Unknown, "com.torrentfree.app"));
        Assert.Null(AndroidStoreRouting.GetLink(AndroidStore.Unknown, "com.torrentfree.app", review: true));
        Assert.False(AndroidStoreRouting.SupportsUpdateCheck(AndroidStore.Unknown));
    }

    [Fact]
    public void CacheIdentityChangesWhenStoreChangesAtSameVersion()
    {
        var play = AndroidStoreRouting.CacheIdentity("1.19", "24", AndroidStore.GooglePlay);
        var galaxy = AndroidStoreRouting.CacheIdentity("1.19", "24", AndroidStore.GalaxyStore);
        Assert.NotEqual(play, galaxy);
        Assert.NotEqual("1.19:24", galaxy);
        var huawei = AndroidStoreRouting.CacheIdentity("1.19", "24", AndroidStore.HuaweiAppGallery);
        Assert.NotEqual(play, huawei);
        Assert.NotEqual(galaxy, huawei);
        Assert.NotEqual("1.19:24", huawei);
    }
}
