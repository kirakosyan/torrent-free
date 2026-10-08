using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFree.Services;

namespace TorrentFree.Models;

public partial class TorrentFileSelectionViewModel : ObservableObject
{
    private IReadOnlyList<TorrentFileChoice> _files = [];
    private int _selectedCount;
    private long _selectedBytes;
    private bool _updatingAll;

    [ObservableProperty]
    public partial IReadOnlyList<TorrentFileChoice> VisibleFiles { get; set; } = Array.Empty<TorrentFileChoice>();

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public bool CanConfirm => _selectedCount > 0;
    public string Summary => string.Format(LocalizationResourceManager.Instance["FileSelectionSummary"],
        _selectedCount, _files.Count, TorrentItem.FormatBytes(_selectedBytes));
    public string[] SelectedPaths => _files.Where(file => file.IsSelected).Select(file => file.Path).ToArray();

    public void Load(IReadOnlyList<TorrentFileChoice> files)
    {
        foreach (var file in _files) file.PropertyChanged -= OnFileChanged;
        _files = files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var file in _files) file.PropertyChanged += OnFileChanged;
        Recount();
        Filter();
    }

    partial void OnSearchTextChanged(string value) => Filter();

    private void Filter() => VisibleFiles = _files.Where(file =>
        file.Path.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

    [RelayCommand]
    private void SelectAll() => SetVisibleSelection(true);

    [RelayCommand]
    private void SelectNone() => SetVisibleSelection(false);

    [RelayCommand]
    private void ToggleFile(TorrentFileChoice? file)
    {
        if (file is not null) file.IsSelected = !file.IsSelected;
    }

    private void SetVisibleSelection(bool selected)
    {
        _updatingAll = true;
        try { foreach (var file in VisibleFiles) file.IsSelected = selected; }
        finally { _updatingAll = false; }
        Recount();
    }

    private void OnFileChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_updatingAll || args.PropertyName != nameof(TorrentFileChoice.IsSelected) || sender is not TorrentFileChoice file) return;
        _selectedCount += file.IsSelected ? 1 : -1;
        _selectedBytes += file.IsSelected ? file.Length : -file.Length;
        RefreshSummary();
    }

    private void Recount()
    {
        _selectedCount = _files.Count(file => file.IsSelected);
        _selectedBytes = _files.Where(file => file.IsSelected).Sum(file => file.Length);
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanConfirm));
    }
}
