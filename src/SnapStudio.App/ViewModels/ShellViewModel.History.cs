using SnapStudio.Core.Documents;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private readonly DocumentHistoryPagination _historyPagination = new(10);

    public int HistoryPageSize => _historyPagination.PageSize;
    public int HistoryPageNumber => _historyPagination.PageIndex + 1;
    public int HistoryPageCount => _historyPagination.PageCount;
    public bool CanShowPreviousHistoryPage => _historyPagination.CanMovePrevious;
    public bool CanShowNextHistoryPage => _historyPagination.CanMoveNext;
    public string HistoryPageLabel => $"Page {HistoryPageNumber} of {HistoryPageCount}";
    public string HistoryRangeLabel => _historyPagination.TotalCount == 0
        ? _allRecentDocuments.Count == 0 ? "No captures yet" : "No matching captures"
        : _historyPagination.TotalCount == 1 ? "1 capture"
        : $"{_historyPagination.Offset + 1}–{_historyPagination.Offset + RecentDocuments.Count} of {_historyPagination.TotalCount} captures";

    public void ShowPreviousHistoryPage()
    {
        if (_historyPagination.MovePrevious()) ApplyRecentDocumentFilter(refreshThumbnails: true);
    }

    public void ShowNextHistoryPage()
    {
        if (_historyPagination.MoveNext()) ApplyRecentDocumentFilter(refreshThumbnails: true);
    }

    private void NotifyHistoryPaginationChanged()
    {
        OnPropertyChanged(nameof(HistoryPageNumber));
        OnPropertyChanged(nameof(HistoryPageCount));
        OnPropertyChanged(nameof(CanShowPreviousHistoryPage));
        OnPropertyChanged(nameof(CanShowNextHistoryPage));
        OnPropertyChanged(nameof(HistoryPageLabel));
        OnPropertyChanged(nameof(HistoryRangeLabel));
    }
}
