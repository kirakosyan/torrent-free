namespace TorrentFree.Services;

public static class AndroidDownloadExportService
{
    public const string PublicDownloadsSubfolder = "TorrentFree";

    public static string GetPublicDownloadsPath(string? childFolder = null)
    {
#if ANDROID
        var externalRoot = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(externalRoot))
        {
            return string.Empty;
        }

        var path = Path.Combine(
            externalRoot,
            AndroidDownloadsDirectory,
            PublicDownloadsSubfolder);

        return string.IsNullOrWhiteSpace(childFolder) ? path : Path.Combine(path, childFolder);
#else
        return string.Empty;
#endif
    }

    public static void EnsurePublicDownloadsFolder()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return;
        }

        var path = GetPublicDownloadsPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(path);
        }
#endif
    }

    public static async Task<string?> ExportToPublicDownloadsAsync(string downloadPath, bool isDirectory)
    {
#if ANDROID
        if (string.IsNullOrWhiteSpace(downloadPath))
        {
            return null;
        }

        if (isDirectory)
        {
            if (!Directory.Exists(downloadPath))
            {
                return null;
            }

            var folderName = GetSafeFolderName(Path.GetFileName(downloadPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            foreach (var sourceFile in Directory.EnumerateFiles(downloadPath, "*", SearchOption.AllDirectories))
            {
                var sourceDirectory = Path.GetDirectoryName(sourceFile) ?? downloadPath;
                var relativeDirectory = Path.GetRelativePath(downloadPath, sourceDirectory);
                var androidRelativePath = BuildAndroidDownloadsRelativePath(folderName, relativeDirectory);
                await CopyFileToAndroidDownloadsAsync(sourceFile, androidRelativePath);
            }

            return GetPublicDownloadsPath(folderName);
        }

        if (!File.Exists(downloadPath))
        {
            return null;
        }

        await CopyFileToAndroidDownloadsAsync(downloadPath, BuildAndroidDownloadsRelativePath());
        return GetPublicDownloadsPath();
#else
        await Task.CompletedTask;
        return null;
#endif
    }

    public static bool TryOpenPublicDownloadsFolder(string? childFolder = null)
    {
#if ANDROID
        var path = GetPublicDownloadsPath(childFolder);
        if (!string.IsNullOrWhiteSpace(path) && TryOpenFolder(path))
        {
            return true;
        }

        return TryOpenRoot("com.android.providers.downloads.documents", "downloads")
               || TryOpenRoot("com.android.externalstorage.documents", "primary")
               || TryOpenFolderPicker(BuildExternalStorageTreeUri(Path.Combine(Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? string.Empty, AndroidDownloadsDirectory)));
#else
        return false;
#endif
    }

    public static bool TryOpenFolder(string folderPath)
    {
#if ANDROID
        var documentUri = BuildExternalStorageDocumentUri(folderPath);
        var treeUri = BuildExternalStorageTreeUri(folderPath);
        if (documentUri is null && treeUri is null)
        {
            return false;
        }

        if (documentUri is not null && TryOpenDirectoryUri(documentUri))
        {
            return true;
        }

        if (treeUri is not null && TryOpenDirectoryUri(treeUri))
        {
            return true;
        }

        return TryOpenFolderPicker(treeUri ?? documentUri);
#else
        return false;
#endif
    }

#if ANDROID
    private static bool TryOpenDirectoryUri(Android.Net.Uri folderUri)
    {
        var viewIntent = new Android.Content.Intent(Android.Content.Intent.ActionView);
        viewIntent.SetDataAndType(folderUri, Android.Provider.DocumentsContract.Document.MimeTypeDir);
        viewIntent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission | Android.Content.ActivityFlags.NewTask);
        return TryStartActivity(viewIntent);
    }

    private static bool TryOpenFolderPicker(Android.Net.Uri? initialUri)
    {
        var treeIntent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocumentTree);
        if (initialUri is not null && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            treeIntent.PutExtra(Android.Provider.DocumentsContract.ExtraInitialUri, initialUri);
        }

        treeIntent.AddFlags(
            Android.Content.ActivityFlags.GrantReadUriPermission |
            Android.Content.ActivityFlags.GrantWriteUriPermission |
            Android.Content.ActivityFlags.GrantPersistableUriPermission |
            Android.Content.ActivityFlags.NewTask);

        return TryStartActivity(treeIntent);
    }

    private static bool TryOpenRoot(string authority, string rootId)
    {
        var rootUri = Android.Provider.DocumentsContract.BuildRootUri(authority, rootId);
        if (rootUri is null)
        {
            return false;
        }

        var viewIntent = new Android.Content.Intent(Android.Content.Intent.ActionView);
        viewIntent.SetData(rootUri);
        viewIntent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission | Android.Content.ActivityFlags.NewTask);

        return TryStartActivity(viewIntent) || TryOpenFolderPicker(rootUri);
    }

    private static bool TryStartActivity(Android.Content.Intent intent)
    {
        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            context.StartActivity(intent);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Android folder intent failed: {ex}");
            return false;
        }
    }

    private static string BuildAndroidDownloadsRelativePath(string? childFolder = null, string? relativeDirectory = null)
    {
        var parts = new List<string>
        {
            AndroidDownloadsDirectory,
            PublicDownloadsSubfolder
        };

        if (!string.IsNullOrWhiteSpace(childFolder))
        {
            parts.Add(childFolder);
        }

        if (!string.IsNullOrWhiteSpace(relativeDirectory) && relativeDirectory != ".")
        {
            parts.AddRange(relativeDirectory
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .Select(GetSafeFolderName)
                .Where(static part => !string.IsNullOrWhiteSpace(part)));
        }

        return string.Join("/", parts) + "/";
    }

    private static async Task CopyFileToAndroidDownloadsAsync(string sourcePath, string relativePath)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            await CopyFileToMediaStoreDownloadsAsync(sourcePath, relativePath);
            return;
        }

        CopyFileToLegacyPublicDownloads(sourcePath, relativePath);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static async Task CopyFileToMediaStoreDownloadsAsync(string sourcePath, string relativePath)
    {
        var context = Android.App.Application.Context;
        var resolver = context.ContentResolver ?? throw new InvalidOperationException("Content resolver is unavailable.");
        var sourceInfo = new FileInfo(sourcePath);
        var displayName = Path.GetFileName(sourcePath);
        var collection = Android.Provider.MediaStore.Downloads.GetContentUri(Android.Provider.MediaStore.VolumeExternalPrimary);

        var existingUri = FindExistingDownload(resolver, collection, displayName, relativePath, sourceInfo.Length);
        if (existingUri is not null)
        {
            return;
        }

        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, displayName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, GetMimeType(displayName));
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, relativePath);
        values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 1);

        var uri = resolver.Insert(collection, values) ?? throw new InvalidOperationException("Failed to create Android download entry.");
        try
        {
            await using (var input = File.OpenRead(sourcePath))
            await using (var output = resolver.OpenOutputStream(uri) ?? throw new InvalidOperationException("Failed to open Android download entry."))
            {
                await input.CopyToAsync(output);
            }

            values.Clear();
            values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
        }
        catch
        {
            try
            {
                resolver.Delete(uri, null, null);
            }
            catch
            {
                // Ignore cleanup failures; the original copy error is more useful.
            }

            throw;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static Android.Net.Uri? FindExistingDownload(
        Android.Content.ContentResolver resolver,
        Android.Net.Uri collection,
        string displayName,
        string relativePath,
        long sourceSize)
    {
        var projection = new[]
        {
            Android.Provider.IBaseColumns.Id,
            Android.Provider.MediaStore.IMediaColumns.Size
        };

        using var cursor = resolver.Query(
            collection,
            projection,
            $"{Android.Provider.MediaStore.IMediaColumns.DisplayName}=? AND {Android.Provider.MediaStore.IMediaColumns.RelativePath}=?",
            [displayName, relativePath],
            null);

        if (cursor is null)
        {
            return null;
        }

        while (cursor.MoveToNext())
        {
            var id = cursor.GetLong(0);
            var size = cursor.GetLong(1);
            var uri = Android.Content.ContentUris.WithAppendedId(collection, id);
            if (size == sourceSize)
            {
                return uri;
            }

            resolver.Delete(uri, null, null);
        }

        return null;
    }

    private static void CopyFileToLegacyPublicDownloads(string sourcePath, string relativePath)
    {
        var publicRoot = Android.OS.Environment.GetExternalStoragePublicDirectory(AndroidDownloadsDirectory)?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(publicRoot))
        {
            throw new InvalidOperationException("Public Downloads folder is unavailable.");
        }

        var relativeParts = relativePath
            .TrimEnd('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .ToArray();
        var destinationDirectory = relativeParts.Length == 0
            ? publicRoot
            : Path.Combine([publicRoot, .. relativeParts]);

        Directory.CreateDirectory(destinationDirectory);

        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destinationPath, overwrite: true);

        Android.Media.MediaScannerConnection.ScanFile(
            Android.App.Application.Context,
            [destinationPath],
            null,
            null);
    }

    private static Android.Net.Uri? BuildExternalStorageDocumentUri(string folderPath)
    {
        var documentId = BuildExternalStorageDocumentId(folderPath);
        return documentId is null
            ? null
            : Android.Provider.DocumentsContract.BuildDocumentUri("com.android.externalstorage.documents", documentId);
    }

    private static Android.Net.Uri? BuildExternalStorageTreeUri(string folderPath)
    {
        var documentId = BuildExternalStorageDocumentId(folderPath);
        return documentId is null
            ? null
            : Android.Provider.DocumentsContract.BuildTreeDocumentUri("com.android.externalstorage.documents", documentId);
    }

    private static string? BuildExternalStorageDocumentId(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        var externalRoot = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(externalRoot))
        {
            return null;
        }

        var fullFolderPath = Path.GetFullPath(folderPath);
        if (!fullFolderPath.StartsWith(externalRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativePath = Path.GetRelativePath(externalRoot, fullFolderPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        return relativePath == "."
            ? "primary:"
            : $"primary:{relativePath}";
    }

    private static string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        return Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? "application/octet-stream";
    }

    private static string GetSafeFolderName(string? folderName)
    {
        var safeName = string.Concat((folderName ?? string.Empty).Where(static c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return string.IsNullOrWhiteSpace(safeName) ? "Download" : safeName;
    }

    private static string AndroidDownloadsDirectory => Android.OS.Environment.DirectoryDownloads ?? "Download";
#endif
}
