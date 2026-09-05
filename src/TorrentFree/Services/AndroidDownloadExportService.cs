namespace TorrentFree.Services;

public static class AndroidDownloadExportService
{
    public const string PublicDownloadsSubfolder = "TorrentFree";
#if ANDROID
    private static readonly Lazy<DownloadExportService> Exporter = new(() => new(
        Path.Combine(FileSystem.AppDataDirectory, "Exports"), new AndroidDownloadExportStore()));
#endif

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

    public static async Task<string?> ExportToPublicDownloadsAsync(string ownerId, string downloadPath, bool isDirectory)
    {
#if ANDROID
        if (string.IsNullOrWhiteSpace(downloadPath))
        {
            return null;
        }

        var ownerSuffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ownerId)))[..12];
        var folderName = GetSafeFolderName(Path.GetFileName(downloadPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))) + "-" + ownerSuffix;
        if (isDirectory)
        {
            if (!Directory.Exists(downloadPath))
            {
                return null;
            }

            foreach (var sourceFile in Directory.EnumerateFiles(downloadPath, "*", SearchOption.AllDirectories))
            {
                var sourceDirectory = Path.GetDirectoryName(sourceFile) ?? downloadPath;
                var relativeDirectory = Path.GetRelativePath(downloadPath, sourceDirectory);
                var androidRelativePath = BuildAndroidDownloadsRelativePath(folderName, relativeDirectory);
                await Exporter.Value.ExportAsync(ownerId, sourceFile, androidRelativePath);
            }

            return GetPublicDownloadsPath(folderName);
        }

        if (!File.Exists(downloadPath))
        {
            return null;
        }

        await Exporter.Value.ExportAsync(ownerId, downloadPath, BuildAndroidDownloadsRelativePath(folderName));
        return GetPublicDownloadsPath(folderName);
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

    private static string GetSafeFolderName(string? folderName)
    {
        var safeName = string.Concat((folderName ?? string.Empty).Where(static c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return string.IsNullOrWhiteSpace(safeName) ? "Download" : safeName;
    }

    private static string AndroidDownloadsDirectory => Android.OS.Environment.DirectoryDownloads ?? "Download";
#endif
}
