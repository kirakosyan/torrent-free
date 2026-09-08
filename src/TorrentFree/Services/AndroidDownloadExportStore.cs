#if ANDROID
namespace TorrentFree.Services;

internal sealed class AndroidDownloadExportStore : IDownloadExportStore
{
    private static Android.Content.ContentResolver Resolver => Android.App.Application.Context.ContentResolver
        ?? throw new InvalidOperationException("Content resolver is unavailable.");

    public Task<Stream?> OpenReadAsync(string id)
    {
        Stream? stream = id.StartsWith("content://", StringComparison.Ordinal)
            ? Resolver.OpenInputStream(Android.Net.Uri.Parse(id)!)
            : (File.Exists(id) ? File.OpenRead(id) : null);
        return Task.FromResult(stream);
    }

    public Task<ExportWriteTarget> CreateAsync(string relativeDirectory, string fileName)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            return Task.FromResult(CreateMediaStoreTarget(relativeDirectory, fileName));

        var externalRoot = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath
            ?? throw new InvalidOperationException("Public Downloads is unavailable.");
        var directory = Path.GetFullPath(Path.Combine(externalRoot, relativeDirectory));
        if (!PathGuard.IsPathWithinDirectory(directory, externalRoot))
            throw new IOException("Invalid export directory.");
        Directory.CreateDirectory(directory);
        for (var suffix = 0; ; suffix++)
        {
            var name = suffix == 0 ? fileName : $"{Path.GetFileNameWithoutExtension(fileName)} ({suffix}){Path.GetExtension(fileName)}";
            var path = Path.Combine(directory, name);
            try { return Task.FromResult(new ExportWriteTarget(path, new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))); }
            catch (IOException) when (File.Exists(path)) { }
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static ExportWriteTarget CreateMediaStoreTarget(string relativeDirectory, string fileName)
    {
        var collection = Android.Provider.MediaStore.Downloads.GetContentUri(Android.Provider.MediaStore.VolumeExternalPrimary);
        using var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, relativeDirectory);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, GetMimeType(fileName));
        values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 1);
        var uri = Resolver.Insert(collection, values) ?? throw new IOException("Could not create the export.");
        try
        {
            // Insert allocates a new entry; Android resolves filename collisions for it.
            return new ExportWriteTarget(uri.ToString()!, Resolver.OpenOutputStream(uri)
                ?? throw new IOException("Could not open the export."));
        }
        catch { Resolver.Delete(uri, null, null); throw; }
    }

    public Task CompleteAsync(string id)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29) && id.StartsWith("content://", StringComparison.Ordinal))
        {
            using var values = new Android.Content.ContentValues();
            values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
            if (Resolver.Update(Android.Net.Uri.Parse(id)!, values, null, null) <= 0)
                throw new IOException("Could not publish the export.");
        }
        else
            Android.Media.MediaScannerConnection.ScanFile(Android.App.Application.Context, [id], null, null);
        return Task.CompletedTask;
    }

    public Task AbortAsync(string id)
    {
        if (id.StartsWith("content://", StringComparison.Ordinal))
            Resolver.Delete(Android.Net.Uri.Parse(id)!, null, null);
        else
            File.Delete(id);
        return Task.CompletedTask;
    }

    private static string GetMimeType(string fileName) => Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(
        Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant()) ?? "application/octet-stream";
}
#endif
