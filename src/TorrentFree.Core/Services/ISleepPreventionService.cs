namespace TorrentFree.Services;

/// <summary>Requests protection from automatic system sleep without keeping the display on.</summary>
public interface ISleepPreventionService
{
    void SetPreventSleep(bool preventSleep);
}
