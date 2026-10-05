namespace TorrentFree.Services;

internal enum AndroidStore { Unknown, GooglePlay, GalaxyStore, HuaweiAppGallery }

internal sealed record AndroidStoreLink(string StorePackage, string AppUri, string? WebUri);

internal static class AndroidStoreRouting
{
    internal const string GooglePlayPackage = "com.android.vending";
    internal const string GalaxyStorePackage = "com.sec.android.app.samsungapps";
    internal const string HuaweiAppGalleryPackage = "com.huawei.appmarket";

    internal static AndroidStore FromInstaller(string? installerPackage) => installerPackage switch
    {
        GooglePlayPackage => AndroidStore.GooglePlay,
        GalaxyStorePackage => AndroidStore.GalaxyStore,
        HuaweiAppGalleryPackage => AndroidStore.HuaweiAppGallery,
        _ => AndroidStore.Unknown
    };

    internal static bool SupportsUpdateCheck(AndroidStore store) => store == AndroidStore.GooglePlay;

    internal static string CacheIdentity(string version, string build, AndroidStore store) =>
        $"{version}:{build}:{store}";

    internal static AndroidStoreLink? GetLink(AndroidStore store, string packageId, bool review = false)
    {
        var id = Uri.EscapeDataString(packageId);
        return store switch
        {
            AndroidStore.GooglePlay => new(GooglePlayPackage,
                "market://details?id=" + id, "https://play.google.com/store/apps/details?id=" + id),
            AndroidStore.GalaxyStore when review => new(GalaxyStorePackage,
                "samsungapps://AppRating/" + id, "https://apps.samsung.com/appquery/AppRating.as?appId=" + id),
            AndroidStore.GalaxyStore => new(GalaxyStorePackage,
                "samsungapps://ProductDetail/" + id, "https://galaxystore.samsung.com/detail/" + id),
            AndroidStore.HuaweiAppGallery => new(HuaweiAppGalleryPackage,
                "appmarket://details?id=" + id, null),
            _ => null
        };
    }
}
