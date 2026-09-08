namespace TorrentFree.Services;

internal static class MauiStoragePaths
{
    public static StoragePaths Create()
    {
        var appData = GetAppDataDirectory();
#if ANDROID
        var downloadBase = Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath ?? appData;
#elif IOS
        var downloadBase = appData;
#else
        var downloadBase = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#endif
        return new StoragePaths(appData, Path.Combine(downloadBase, "TorrentFree", "Downloads"));
    }

    // On Windows the MAUI FileSystem.AppDataDirectory points to the MSIX package sandbox
    // (LocalState) when running packaged, but to a different temp path when unpackaged.
    // Using LocalApplicationData + app subfolder gives a consistent, user-scoped path in
    // both modes so data is never lost when switching between Debug (unpackaged) and a
    // deployed MSIX build.
    private static string GetAppDataDirectory()
    {
#if WINDOWS
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TorrentFree");
        Directory.CreateDirectory(dir);

        // One-time migration: copy data from the old MSIX sandbox path if the new location
        // is empty and the old one has data (happens on first run after this change).
        MigrateFromMsixSandboxIfNeeded(dir);

        return dir;
#else
        return FileSystem.AppDataDirectory;
#endif
    }

#if WINDOWS
    private static void MigrateFromMsixSandboxIfNeeded(string newDir)
    {
        try
        {
            var newFile = Path.Combine(newDir, "torrents.json");
            var legacyFiles = GetMsixLocalStateDirectories()
                .Select(static directory => Path.Combine(directory, "torrents.json"));

            if (StorageMigration.TryMigrate(newFile, legacyFiles))
            {
                System.Diagnostics.Debug.WriteLine($"Migrated packaged app state to {newFile}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Data migration error: {ex.Message}");
        }
    }

    private static IReadOnlyCollection<string> GetMsixLocalStateDirectories()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // This is the authoritative path while the process has package identity.
        // Accessing ApplicationData.Current is unsupported for an unpackaged process,
        // so keep the fallback completely non-fatal for local/debug builds.
        try
        {
            AddDirectory(Windows.Storage.ApplicationData.Current.LocalFolder.Path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Packaged LocalState is unavailable: {ex.Message}");
        }

        // Resolve the folder from the package family name as a fallback. The family
        // name includes the publisher-derived suffix and therefore matches the real
        // Packages directory rather than the cross-platform ApplicationId.
        try
        {
            var packageFamilyName = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            if (!string.IsNullOrWhiteSpace(packageFamilyName))
            {
                AddDirectory(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages",
                    packageFamilyName,
                    "LocalState"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Package identity is unavailable: {ex.Message}");
        }

        return directories;

        void AddDirectory(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                directories.Add(path);
            }
        }
    }
#endif

}
