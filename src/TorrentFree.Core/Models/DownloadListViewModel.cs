using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFree.Services;

namespace TorrentFree.Models;

public enum DownloadFilter { All, Active, Completed, Paused, NeedsAttention }

public sealed class DownloadFilterOption(DownloadFilter filter, string resourceKey) : ObservableObject
{
    public DownloadFilter Filter { get; } = filter;
    public string Label => LocalizationResourceManager.Instance[resourceKey];
    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>The live, filtered projection of the queue. Filtering never changes transfer state.</summary>
public partial class DownloadListViewModel : DownloadSelectionViewModel, IDisposable
{
    private readonly ObservableCollection<TorrentItem> _torrents;
    private readonly HashSet<TorrentItem> _observed = [];
    private bool _disposed;

    public ObservableCollection<TorrentItem> DisplayTorrents { get; } = [];
    public IReadOnlyList<DownloadFilterOption> DownloadFilters { get; } =
    [
        new(DownloadFilter.All, "FilterAllDownloads"),
        new(DownloadFilter.Active, "FilterActiveDownloads"),
        new(DownloadFilter.Completed, "StatusCompleted"),
        new(DownloadFilter.Paused, "StatusPaused"),
        new(DownloadFilter.NeedsAttention, "FilterNeedsAttention")
    ];

    [ObservableProperty]
    public partial string DownloadSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DownloadFilterOption? SelectedDownloadFilter { get; set; }

    [ObservableProperty]
    public partial bool SortByStatus { get; set; }

    public bool ShowDownloadSearch => _torrents.Count > 5;
    public bool NoMatchingDownloads => ShowDownloadSearch && DisplayTorrents.Count == 0;

    public DownloadListViewModel(ObservableCollection<TorrentItem> torrents)
    {
        _torrents = torrents;
        SelectedDownloadFilter = DownloadFilters[0];
        _torrents.CollectionChanged += OnQueueChanged;
        LocalizationResourceManager.Instance.PropertyChanged += OnLanguageChanged;
        ObserveQueue();
        RefreshDownloads();
    }

    partial void OnDownloadSearchTextChanged(string value)
    {
        if (!ShowDownloadSearch && !string.IsNullOrEmpty(value)) DownloadSearchText = string.Empty;
        else RefreshDownloads();
    }
    partial void OnSelectedDownloadFilterChanged(DownloadFilterOption? value)
    {
        if (!ShowDownloadSearch && value != DownloadFilters[0]) SelectedDownloadFilter = DownloadFilters[0];
        else RefreshDownloads();
    }
    partial void OnSortByStatusChanged(bool value)
    {
        RefreshDownloads();
        OnSortPreferenceChanged();
    }

    protected virtual void OnSortPreferenceChanged() { }

    [RelayCommand]
    private void ClearDownloadFilters()
    {
        DownloadSearchText = string.Empty;
        SelectedDownloadFilter = DownloadFilters[0];
    }

    protected override void OnOpeningDownloadNotification(TorrentItem torrent)
    {
        if (_torrents.Contains(torrent) && !Matches(torrent))
            ClearDownloadFilters();
    }

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveQueue();
        // Attach platform commands before publishing new rows to the UI. The command
        // properties are intentionally not persisted or observable on TorrentItem.
        OnDownloadsChanged(e);
        if (!ShowDownloadSearch) ClearDownloadFilters();
        RefreshDownloads();
        OnPropertyChanged(nameof(ShowDownloadSearch));
    }

    protected virtual void OnDownloadsChanged(NotifyCollectionChangedEventArgs e) { }

    private void ObserveQueue()
    {
        foreach (var removed in _observed.Where(t => !_torrents.Contains(t)).ToArray())
        {
            removed.PropertyChanged -= OnTorrentChanged;
            _observed.Remove(removed);
        }
        foreach (var torrent in _torrents)
            if (_observed.Add(torrent)) torrent.PropertyChanged += OnTorrentChanged;
    }

    private void OnTorrentChanged(object? sender, PropertyChangedEventArgs e)
    {
        var filter = ShowDownloadSearch ? SelectedDownloadFilter?.Filter ?? DownloadFilter.All : DownloadFilter.All;
        var needsRefresh = e.PropertyName switch
        {
            nameof(TorrentItem.Name) => SortByStatus || (ShowDownloadSearch && !string.IsNullOrWhiteSpace(DownloadSearchText)),
            nameof(TorrentItem.Status) => SortByStatus || filter != DownloadFilter.All,
            nameof(TorrentItem.Progress) or nameof(TorrentItem.DateCompleted) => filter == DownloadFilter.Completed,
            _ => false
        };
        if (needsRefresh)
            RefreshDownloads();
    }

    private bool Matches(TorrentItem torrent)
    {
        if (!ShowDownloadSearch) return true;
        var search = DownloadSearchText?.Trim();
        if (!string.IsNullOrEmpty(search) && !(torrent.Name ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase))
            return false;
        return (SelectedDownloadFilter?.Filter ?? DownloadFilter.All) switch
        {
            DownloadFilter.Active => torrent.Status is DownloadStatus.Downloading or DownloadStatus.Seeding,
            DownloadFilter.Completed => torrent.Progress >= 100 || torrent.DateCompleted is not null
                || torrent.Status is DownloadStatus.Completed or DownloadStatus.Seeding,
            DownloadFilter.Paused => torrent.Status == DownloadStatus.Paused,
            DownloadFilter.NeedsAttention => torrent.Status is DownloadStatus.Failed or DownloadStatus.WaitingForWifi,
            _ => true
        };
    }

    private void RefreshDownloads()
    {
        IEnumerable<TorrentItem> visible = _torrents.Where(Matches);
        if (SortByStatus)
            visible = visible.OrderBy(t => StatusOrder(t.Status)).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var ordered = visible.ToList();
        // Reconcile rather than replacing the collection, preserving scroll and row identity.
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].DisplayIndex = i;
            if (i < DisplayTorrents.Count && ReferenceEquals(DisplayTorrents[i], ordered[i])) continue;
            var previousIndex = DisplayTorrents.IndexOf(ordered[i]);
            if (previousIndex >= 0) DisplayTorrents.Move(previousIndex, i);
            else DisplayTorrents.Insert(i, ordered[i]);
        }
        while (DisplayTorrents.Count > ordered.Count) DisplayTorrents.RemoveAt(DisplayTorrents.Count - 1);
        OnPropertyChanged(nameof(NoMatchingDownloads));
    }

    private static int StatusOrder(DownloadStatus status) => status switch
    {
        DownloadStatus.Downloading => 0,
        DownloadStatus.Seeding => 1,
        DownloadStatus.Paused => 2,
        DownloadStatus.Stopped => 3,
        DownloadStatus.Queued => 4,
        DownloadStatus.WaitingForWifi => 5,
        DownloadStatus.Completed => 6,
        DownloadStatus.Failed => 7,
        _ => 8
    };

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var option in DownloadFilters) option.RefreshLabel();
        foreach (var torrent in _observed) torrent.RefreshLocalizableProperties();
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _torrents.CollectionChanged -= OnQueueChanged;
        LocalizationResourceManager.Instance.PropertyChanged -= OnLanguageChanged;
        foreach (var torrent in _observed) torrent.PropertyChanged -= OnTorrentChanged;
        _observed.Clear();
        GC.SuppressFinalize(this);
    }
}
