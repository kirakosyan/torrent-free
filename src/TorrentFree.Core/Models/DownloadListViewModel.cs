using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFree.Services;

namespace TorrentFree.Models;

public enum DownloadFilter { All, Active, Completed, Paused, NeedsAttention }

public sealed class DownloadFilterOption(DownloadFilter filter, string resourceKey)
{
    public DownloadFilter Filter { get; } = filter;
    public string Label => LocalizationResourceManager.Instance[resourceKey];
}

/// <summary>The live, filtered projection of the queue. Filtering never changes transfer state.</summary>
public partial class DownloadListViewModel : DownloadSelectionViewModel, IDisposable
{
    private readonly ObservableCollection<TorrentItem> _torrents;
    private readonly HashSet<TorrentItem> _observed = [];
    private HashSet<TorrentItem> _visible = [];
    private bool _updatingFilters;
    private bool _initialized;
    private bool _disposed;

    public ObservableCollection<TorrentItem> DisplayTorrents { get; } = [];
    public IReadOnlyList<DownloadFilterOption> DownloadFilters { get; private set; } = CreateDownloadFilters();

    private static IReadOnlyList<DownloadFilterOption> CreateDownloadFilters() =>
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
    public bool ShowDownloadList => DisplayTorrents.Count > 0;
    public bool IsEmpty => _torrents.Count == 0;
    public bool ShowEmptyDownloads => IsEmpty && !HasInitializationError;
    public bool HasInitializationError => !string.IsNullOrWhiteSpace(InitializationErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInitializationError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyDownloads))]
    public partial string? InitializationErrorMessage { get; set; }

    public DownloadListViewModel(ObservableCollection<TorrentItem> torrents, bool initialize = true)
    {
        _torrents = torrents;
        ResetFilters();
        if (initialize) InitializeDownloads();
    }

    protected void InitializeDownloads()
    {
        if (_initialized) return;
        _initialized = true;
        _torrents.CollectionChanged += OnQueueChanged;
        LocalizationResourceManager.Instance.PropertyChanged += OnLanguageChanged;
        ObserveQueue();
        RefreshDownloads();
    }

    partial void OnDownloadSearchTextChanged(string value)
    {
        OnFilterChanged();
    }
    partial void OnSelectedDownloadFilterChanged(DownloadFilterOption? value)
    {
        OnFilterChanged();
    }
    private void OnFilterChanged()
    {
        if (_updatingFilters) return;
        if (!ShowDownloadSearch) ResetFilters();
        RefreshDownloads();
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
        ResetFilters();
        RefreshDownloads();
    }

    private void ResetFilters()
    {
        _updatingFilters = true;
        try
        {
            DownloadSearchText = string.Empty;
            SelectedDownloadFilter = DownloadFilters[0];
        }
        finally { _updatingFilters = false; }
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
        if (!ShowDownloadSearch) ResetFilters();
        RefreshDownloads();
        OnPropertyChanged(nameof(ShowDownloadSearch));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowEmptyDownloads));
    }

    protected virtual void OnDownloadsChanged(NotifyCollectionChangedEventArgs e) { }
    protected virtual void OnTorrentObserved(TorrentItem torrent) { }
    protected virtual void OnTorrentUnobserved(TorrentItem torrent) { }

    private void ObserveQueue()
    {
        var current = _torrents.ToHashSet();
        foreach (var removed in _observed.Where(t => !current.Contains(t)).ToArray())
        {
            removed.PropertyChanged -= OnTorrentChanged;
            _observed.Remove(removed);
            OnTorrentUnobserved(removed);
        }
        foreach (var torrent in _torrents)
        {
            if (!_observed.Add(torrent)) continue;
            torrent.PropertyChanged += OnTorrentChanged;
            OnTorrentObserved(torrent);
        }
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
        // Ordinary progress ticks do not change membership or ordering.
        if (needsRefresh && sender is TorrentItem torrent
            && ((SortByStatus && e.PropertyName is nameof(TorrentItem.Status) or nameof(TorrentItem.Name))
                || Matches(torrent) != _visible.Contains(torrent)))
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
        if (!_initialized) return;
        IEnumerable<TorrentItem> visible = _torrents.Where(Matches);
        if (SortByStatus)
            visible = visible.OrderBy(t => StatusOrder(t.Status)).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var ordered = visible.ToList();
        var previouslyVisible = _visible;
        _visible = ordered.ToHashSet();
        if (SelectedTorrent is { } selected && !_visible.Contains(selected))
            SelectedTorrent = ordered.FirstOrDefault(t => t.Id == selected.Id);
        // Reconcile rather than replacing the collection, preserving scroll and row identity.
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].DisplayIndex = i;
            if (i < DisplayTorrents.Count && ReferenceEquals(DisplayTorrents[i], ordered[i])) continue;
            var previousIndex = previouslyVisible.Contains(ordered[i]) ? DisplayTorrents.IndexOf(ordered[i]) : -1;
            if (previousIndex >= 0) DisplayTorrents.Move(previousIndex, i);
            else DisplayTorrents.Insert(i, ordered[i]);
        }
        while (DisplayTorrents.Count > ordered.Count) DisplayTorrents.RemoveAt(DisplayTorrents.Count - 1);
        OnPropertyChanged(nameof(NoMatchingDownloads));
        OnPropertyChanged(nameof(ShowDownloadList));
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
        var filter = SelectedDownloadFilter?.Filter ?? DownloadFilter.All;
        _updatingFilters = true;
        try
        {
            // Native pickers cache item labels, so replace their source on language changes.
            DownloadFilters = CreateDownloadFilters();
            OnPropertyChanged(nameof(DownloadFilters));
            SelectedDownloadFilter = DownloadFilters.Single(option => option.Filter == filter);
        }
        finally { _updatingFilters = false; }
        foreach (var torrent in _observed) torrent.RefreshLocalizableProperties();
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _torrents.CollectionChanged -= OnQueueChanged;
        LocalizationResourceManager.Instance.PropertyChanged -= OnLanguageChanged;
        foreach (var torrent in _observed)
        {
            torrent.PropertyChanged -= OnTorrentChanged;
            OnTorrentUnobserved(torrent);
        }
        _observed.Clear();
        _visible.Clear();
        GC.SuppressFinalize(this);
    }
}
