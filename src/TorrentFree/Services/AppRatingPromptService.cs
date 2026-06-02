using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using TorrentFree.Models;

namespace TorrentFree.Services;

public interface IAppRatingPromptService
{
    Task NotifySuccessfulDownloadAsync();
}

public sealed class AppRatingPromptService : IAppRatingPromptService
{
    private readonly IStorageService _storageService;
    private readonly IStoreReviewLauncher _storeReviewLauncher;
    private readonly SemaphoreSlim _promptLock = new(1, 1);

    public AppRatingPromptService(IStorageService storageService, IStoreReviewLauncher storeReviewLauncher)
    {
        _storageService = storageService;
        _storeReviewLauncher = storeReviewLauncher;
    }

    public async Task NotifySuccessfulDownloadAsync()
    {
        await _promptLock.WaitAsync();
        try
        {
            var utcNow = DateTime.UtcNow;
            var settings = await _storageService.LoadSettingsAsync();

            AppRatingPromptRules.RecordSuccessfulDownload(settings);
            await _storageService.SaveSettingsAsync(settings);

            if (!_storeReviewLauncher.IsSupported || !AppRatingPromptRules.ShouldPrompt(settings, utcNow))
            {
                return;
            }

            var shouldRate = await AskToRateAsync();
            if (shouldRate is null)
            {
                return;
            }

            settings = await _storageService.LoadSettingsAsync();
            if (shouldRate.Value)
            {
                AppRatingPromptRules.RecordPromptAccepted(settings);
                await _storageService.SaveSettingsAsync(settings);

                var opened = await _storeReviewLauncher.OpenReviewPageAsync();
                if (!opened)
                {
                    settings = await _storageService.LoadSettingsAsync();
                    settings.HasAcceptedRatingPrompt = false;
                    AppRatingPromptRules.RecordPromptDeclined(settings, utcNow);
                    await _storageService.SaveSettingsAsync(settings);
                }
            }
            else
            {
                AppRatingPromptRules.RecordPromptDeclined(settings, utcNow);
                await _storageService.SaveSettingsAsync(settings);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App rating prompt error: {ex}");
        }
        finally
        {
            _promptLock.Release();
        }
    }

    private static Task<bool?> AskToRateAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current is null)
            {
                return (bool?)null;
            }

            return await Shell.Current.DisplayAlertAsync(
                LocalizationResourceManager.Instance["RateAppTitle"],
                LocalizationResourceManager.Instance["RateAppMessage"],
                LocalizationResourceManager.Instance["RateAppButton"],
                LocalizationResourceManager.Instance["NotNow"]);
        });
    }
}
