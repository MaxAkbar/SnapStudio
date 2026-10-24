using SnapStudio.Core.Documents;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class DocumentHistoryPaginationTests
{
    [TestMethod]
    public void EmptyHistoryAndSinglePageDisableBothDirections()
    {
        var paging = new DocumentHistoryPagination(10);
        Assert.AreEqual(1, paging.PageCount);
        Assert.IsFalse(paging.MovePrevious());
        Assert.IsFalse(paging.MoveNext());
        paging.UpdateCount(10);
        Assert.AreEqual(1, paging.PageCount);
        Assert.AreEqual(0, paging.Offset);
        Assert.IsFalse(paging.CanMovePrevious);
        Assert.IsFalse(paging.CanMoveNext);
    }

    [TestMethod]
    [DataRow(11, 2)]
    [DataRow(20, 2)]
    [DataRow(25, 3)]
    [DataRow(37, 4)]
    public void BrowsingEveryPageHasNoGapsOrDuplicateItems(int count, int pages)
    {
        var paging = new DocumentHistoryPagination(10);
        paging.UpdateCount(count);
        int[] items = Enumerable.Range(0, count).ToArray();
        var visited = new List<int>();
        do
        {
            int[] visible = items.Skip(paging.Offset).Take(paging.PageSize).ToArray();
            Assert.IsTrue(visible.Length <= 10);
            visited.AddRange(visible);
        } while (paging.MoveNext());

        Assert.AreEqual(pages, paging.PageCount);
        Assert.AreSequenceEqual(items, visited);
        Assert.IsFalse(paging.CanMoveNext);
        Assert.IsFalse(paging.MoveNext());
        while (paging.MovePrevious()) { }
        Assert.AreEqual(0, paging.PageIndex);
        Assert.AreEqual(0, paging.Offset);
        Assert.IsFalse(paging.CanMovePrevious);
    }

    [TestMethod]
    public void RemovingTheLastPageClampsToAnExistingPage()
    {
        var paging = new DocumentHistoryPagination(10);
        paging.UpdateCount(21);
        paging.MoveNext();
        paging.MoveNext();
        Assert.AreEqual(2, paging.PageIndex);
        paging.UpdateCount(20);
        Assert.AreEqual(1, paging.PageIndex);
        Assert.AreEqual(10, paging.Offset);
        paging.UpdateCount(0);
        Assert.AreEqual(0, paging.PageIndex);
        Assert.IsFalse(paging.CanMoveNext);
        Assert.IsFalse(paging.CanMovePrevious);
    }

    [TestMethod]
    public void SearchAndNewCapturesCanResetWhileOrdinaryRefreshPreservesThePage()
    {
        var paging = new DocumentHistoryPagination(10);
        paging.UpdateCount(50);
        paging.MoveNext();
        paging.MoveNext();
        paging.UpdateCount(50);
        Assert.AreEqual(2, paging.PageIndex);
        paging.UpdateCount(51, resetPage: true);
        Assert.AreEqual(0, paging.PageIndex);
        paging.MoveNext();
        paging.Reset();
        paging.UpdateCount(2);
        Assert.AreEqual(0, paging.PageIndex);
        Assert.AreEqual(1, paging.PageCount);
    }

    [TestMethod]
    public void InvalidCountsAndPageSizesAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DocumentHistoryPagination(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DocumentHistoryPagination(-1));
        var paging = new DocumentHistoryPagination();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => paging.UpdateCount(-1));
    }
}
