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
        await OpenStoreAsync("ms-windows-store://pdp/?ProductId=9MX3S655HWN7",
            "https://apps.microsoft.com/detail/9MX3S655HWN7");
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
            await OpenStoreAsync("market://details?id=com.libronest.app",
                "https://play.google.com/store/apps/details?id=com.libronest.app");
            return;
        }
        var uris = new List<Android.Net.Uri>();
        foreach (var file in audioFiles) uris.Add(await GetAudioUriAsync(file));
        var clip = Android.Content.ClipData.NewRawUri("Audiobook", uris[0])
            ?? throw new IOException("Could not grant access to the audio files.");
        foreach (var uri in uris.Skip(1)) clip.AddItem(new Android.Content.ClipData.Item(uri));
        intent.ClipData = clip;
        if (uris.Count == 1) intent.SetDataAndType(uris[0], "audio/*");
        else intent.PutParcelableArrayListExtra(Android.Content.Intent.ExtraStream,
            uris.Cast<Android.OS.IParcelable>().ToList());
        context.StartActivity(intent);
#else
        throw new PlatformNotSupportedException();
#endif
    }

#if ANDROID
    private static async Task<Android.Net.Uri> GetAudioUriAsync(string file)
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
            var directory = Path.Combine(FileSystem.CacheDirectory, "LibroNestHandoff", Guid.NewGuid().ToString("N"));
            var staged = Path.Combine(directory, Path.GetFileName(file));
            Directory.CreateDirectory(directory);
            try
            {
                await using (var input = File.OpenRead(file))
                await using (var output = File.Create(staged))
                    await input.CopyToAsync(output);
                return AndroidX.Core.Content.FileProvider.GetUriForFile(context, authority, new Java.IO.File(staged))
                    ?? throw new IOException("Could not share the audio file.");
            }
            catch
            {
                File.Delete(staged);
                Directory.Delete(directory);
                throw;
            }
        }
    }
#endif

    private static async Task OpenStoreAsync(string nativeUri, string webUri)
    {
        try { if (await Launcher.Default.TryOpenAsync(nativeUri)) return; }
        catch (Exception) { /* A browser can still open the listing without a store client. */ }
        if (!await Launcher.Default.TryOpenAsync(webUri)) throw new IOException("The store could not be opened.");
    }
}
