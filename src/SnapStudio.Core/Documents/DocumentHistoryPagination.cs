namespace SnapStudio.Core.Documents;

public sealed class DocumentHistoryPagination
{
    public DocumentHistoryPagination(int pageSize = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        PageSize = pageSize;
    }

    public int PageSize { get; }
    public int PageIndex { get; private set; }
    public int TotalCount { get; private set; }
    public int PageCount => TotalCount == 0 ? 1 : 1 + (TotalCount - 1) / PageSize;
    public int Offset => PageIndex * PageSize;
    public bool CanMovePrevious => PageIndex > 0;
    public bool CanMoveNext => PageIndex < PageCount - 1;

    public void UpdateCount(int totalCount, bool resetPage = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        TotalCount = totalCount;
        PageIndex = resetPage ? 0 : Math.Min(PageIndex, PageCount - 1);
    }

    public bool MovePrevious()
    {
        if (!CanMovePrevious) return false;
        PageIndex--;
        return true;
    }

    public bool MoveNext()
    {
        if (!CanMoveNext) return false;
        PageIndex++;
        return true;
    }

    public void Reset() => PageIndex = 0;
}
