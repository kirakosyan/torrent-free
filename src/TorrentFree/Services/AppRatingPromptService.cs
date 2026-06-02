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
            var settings = await _storageService.UpdateRatingPromptStateAsync(current =>
            {
                AppRatingPromptRules.RecordSuccessfulDownload(current);
                return current;
            });

            if (!_storeReviewLauncher.IsSupported || !AppRatingPromptRules.ShouldPrompt(settings, DateTime.UtcNow))
            {
                return;
            }

            var shouldRate = await AskToRateAsync();
            if (shouldRate is null)
            {
                return;
            }

            if (shouldRate.Value)
            {
                await _storageService.UpdateRatingPromptStateAsync(current =>
                {
                    AppRatingPromptRules.RecordPromptAccepted(current);
                    return current;
                });

                var opened = await _storeReviewLauncher.OpenReviewPageAsync();
                if (!opened)
                {
                    await _storageService.UpdateRatingPromptStateAsync(current =>
                    {
                        current.HasAcceptedRatingPrompt = false;
                        AppRatingPromptRules.RecordPromptDeclined(current, DateTime.UtcNow);
                        return current;
                    });
                }
            }
            else
            {
                await _storageService.UpdateRatingPromptStateAsync(current =>
                {
                    AppRatingPromptRules.RecordPromptDeclined(current, DateTime.UtcNow);
                    return current;
                });
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
