using TorrentFree.Models;

namespace TorrentFree.Services;

/// <summary>
/// Serializes read-modify-write settings updates made by the singleton view models.
/// </summary>
internal static class AppSettingsPersistence
{
    private static readonly SemaphoreSlim UpdateLock = new(1, 1);

    public static async Task RunExclusiveAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await UpdateLock.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    public static async Task<AppSettings> LoadAsync(IStorageService storageService)
    {
        ArgumentNullException.ThrowIfNull(storageService);

        await UpdateLock.WaitAsync();
        try
        {
            return await storageService.LoadSettingsAsync();
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    public static async Task<AppSettings> MergeAndSaveAsync(
        IStorageService storageService,
        Func<AppSettings, AppSettings> merge)
    {
        ArgumentNullException.ThrowIfNull(storageService);
        ArgumentNullException.ThrowIfNull(merge);

        await UpdateLock.WaitAsync();
        try
        {
            var current = await storageService.LoadSettingsAsync();
            var updated = merge(current);
            await storageService.SaveSettingsAsync(updated);
            return updated;
        }
        finally
        {
            UpdateLock.Release();
        }
    }
}
