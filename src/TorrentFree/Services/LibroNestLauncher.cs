namespace TorrentFree.Services;

public sealed class LibroNestLauncher : ILibroNestLauncher
{
    public bool IsSupported => DeviceInfo.Platform == DevicePlatform.WinUI || DeviceInfo.Platform == DevicePlatform.Android;

    public async Task OpenAsync(string downloadPath, IReadOnlyList<string> audioFiles)
    {
        if (audioFiles.Count == 0) throw new IOException("No supported audio files remain.");
#if WINDOWS
        const string family = "9971ArmenKirakosyan.LibroNest_5yzvegktgaz4g";
        var uri = new Uri("libronest://import?path=" + Uri.EscapeDataString(downloadPath));
        var support = await Windows.System.Launcher.QueryUriSupportAsync(uri,
            Windows.System.LaunchQuerySupportType.Uri, family);
        if (support == Windows.System.LaunchQuerySupportStatus.Available)
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(uri,
                new Windows.System.LauncherOptions { TargetApplicationPackageFamilyName = family }))
                throw new IOException("LibroNest could not be opened.");
            return;
        }
        // Older LibroNest releases already support single-file activation.
        if (audioFiles.Count == 1)
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(audioFiles[0]);
            if (await Windows.System.Launcher.QueryFileSupportAsync(file, family)
                == Windows.System.LaunchQuerySupportStatus.Available)
            {
                if (!await Windows.System.Launcher.LaunchFileAsync(file,
                    new Windows.System.LauncherOptions { TargetApplicationPackageFamilyName = family }))
                    throw new IOException("LibroNest could not be opened.");
                return;
            }
        }
        await OpenStoreAsync();
#elif ANDROID
        var context = Android.App.Application.Context;
        using var intent = new Android.Content.Intent(audioFiles.Count == 1
            ? Android.Content.Intent.ActionView : Android.Content.Intent.ActionSendMultiple);
        intent.SetPackage("com.libronest.app");
        intent.SetType("audio/*");
        if (audioFiles.Count == 1)
            intent.SetDataAndType(Android.Net.Uri.Parse("content://" + context.PackageName + ".audiobooks/audio"), "audio/*");
        intent.AddFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.GrantReadUriPermission);
        if (intent.ResolveActivity(context.PackageManager!) is null)
        {
            await OpenStoreAsync();
            return;
        }
        var uris = new List<Android.Net.Uri>();
        foreach (var file in audioFiles) uris.Add(await GetAudioUriAsync(file));
        var clip = Android.Content.ClipData.NewRawUri("Audiobook", uris[0])
            ?? throw new IOException("Could not grant access to the audio files.");
        foreach (var uri in uris.Skip(1)) clip.AddItem(new Android.Content.ClipData.Item(uri));
        intent.ClipData = clip;
        if (Directory.Exists(downloadPath))
        {
            intent.PutExtra("com.libronest.import.TITLE", Path.GetFileName(downloadPath.TrimEnd(Path.DirectorySeparatorChar)));
            intent.PutExtra("com.libronest.import.RELATIVE_PATHS",
                audioFiles.Select(file => Path.GetRelativePath(downloadPath, file)).ToArray());
        }
        if (uris.Count == 1) intent.SetDataAndType(uris[0], "audio/*");
        else intent.PutParcelableArrayListExtra(Android.Content.Intent.ExtraStream,
            uris.Cast<Android.OS.IParcelable>().ToList());
        context.StartActivity(intent);
#else
        throw new PlatformNotSupportedException();
#endif
    }

#if ANDROID
    private readonly AudiobookHandoffCache handoffCache = new(Path.Combine(FileSystem.CacheDirectory, "LibroNestHandoff"));

    private async Task<Android.Net.Uri> GetAudioUriAsync(string file)
    {
        var context = Android.App.Application.Context;
        var authority = context.PackageName + ".audiobooks";
        try
        {
            return AndroidX.Core.Content.FileProvider.GetUriForFile(context, authority, new Java.IO.File(file))
                ?? throw new IOException("Could not share the audio file.");
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            // Older downloads may live beside a picked torrent or in a custom folder.
            // Stage only that selected track instead of exposing broad filesystem roots.
            var staged = await handoffCache.StageAsync(file);
            return AndroidX.Core.Content.FileProvider.GetUriForFile(context, authority, new Java.IO.File(staged))
                ?? throw new IOException("Could not share the audio file.");
        }
    }
#endif

    private static async Task OpenStoreAsync()
    {
        if (!await PlatformAppStore.OpenListingAsync("9MX3S655HWN7", "com.libronest.app"))
            throw new IOException("The store could not be opened.");
    }
}
