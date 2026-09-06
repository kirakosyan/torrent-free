using System.Text.Json;

namespace TorrentFree.Services;

public sealed record AppPromptState
{
    // Hashes only; filenames, paths and magnet links are never stored here or sent to a store.
    public string[] CompletedDownloadIds { get; init; } = [];
    public bool ReviewPromptsDisabled { get; init; }
    public bool ReviewSubmitted { get; init; }
    public DateTimeOffset? LastReviewPromptUtc { get; init; }
    public int DownloadsAtLastReviewPrompt { get; init; }
    public string? CheckedInstalledVersion { get; init; }
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }
    public AppUpdateAvailability UpdateAvailability { get; init; }
}

public interface IAppPromptStateStore
{
    Task<AppPromptState> LoadAsync();
    Task SaveAsync(AppPromptState state);
}

/// <summary>Separate from torrent/settings snapshots so unrelated saves cannot undo an opt-out.</summary>
public sealed class AppPromptStateStore(StoragePaths paths) : IAppPromptStateStore
{
    private readonly string _path = Path.Combine(paths.AppDataDirectory, "store-prompts.json");

    public async Task<AppPromptState> LoadAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<AppPromptState>(json)
                ?? throw new JsonException("Missing store prompt state.");
            if (state.CompletedDownloadIds is null)
                throw new JsonException("Missing completed download identities.");
            return state;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        // Do not reset corrupt/unreadable state: that could erase a user's opt-out.
    }

    public async Task SaveAsync(AppPromptState state)
    {
        Directory.CreateDirectory(paths.AppDataDirectory);
        var temporaryPath = _path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(state)).ConfigureAwait(false);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
