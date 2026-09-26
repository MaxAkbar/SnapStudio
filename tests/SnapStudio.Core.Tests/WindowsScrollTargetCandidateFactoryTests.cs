using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsScrollTargetCandidateFactoryTests
{
    [TestMethod]
    public void TryCreate_WhenDescriptorIsScrollable_ReturnsCandidateWithMetadata()
    {
        var descriptor = new WindowsScrollTargetDescriptor(
            "42.1",
            "Document area",
            "DocumentHost",
            "Chrome_RenderWidgetHostHWND",
            "ControlType.Document",
            1234,
            5678,
            new RectD(10, 20, 800, 600),
            IsVerticallyScrollable: true,
            IsHorizontallyScrollable: false,
            VerticalScrollPercent: 25,
            HorizontalScrollPercent: -1);

        bool created = WindowsScrollTargetCandidateFactory.TryCreate(
            descriptor,
            out ScrollTargetCandidate? candidate);

        Assert.IsTrue(created);
        Assert.IsNotNull(candidate);
        Assert.AreEqual("uia:42.1", candidate.Id);
        Assert.AreEqual("Document area", candidate.DisplayName);
        Assert.AreEqual(ScrollTargetKind.DocumentViewer, candidate.Kind);
        Assert.AreEqual("Chrome_RenderWidgetHostHWND", candidate.Metadata["className"]);
        Assert.AreEqual("True", candidate.Metadata["isVerticallyScrollable"]);
    }

    [TestMethod]
    public void TryCreate_WhenDescriptorIsNotScrollable_ReturnsFalse()
    {
        var descriptor = new WindowsScrollTargetDescriptor(
            "42.1",
            "Static area",
            string.Empty,
            "Static",
            "ControlType.Text",
            1234,
            0,
            new RectD(10, 20, 800, 600),
            IsVerticallyScrollable: false,
            IsHorizontallyScrollable: false,
            VerticalScrollPercent: -1,
            HorizontalScrollPercent: -1);

        bool created = WindowsScrollTargetCandidateFactory.TryCreate(
            descriptor,
            out ScrollTargetCandidate? candidate);

        Assert.IsFalse(created);
        Assert.IsNull(candidate);
    }

    [TestMethod]
    public void TryCreate_WhenBoundsAreInvalid_ReturnsFalse()
    {
        var descriptor = new WindowsScrollTargetDescriptor(
            "42.1",
            "Invalid area",
            string.Empty,
            "Pane",
            "ControlType.Pane",
            1234,
            0,
            new RectD(10, 20, 1, 600),
            IsVerticallyScrollable: true,
            IsHorizontallyScrollable: false,
            VerticalScrollPercent: 0,
            HorizontalScrollPercent: -1);

        bool created = WindowsScrollTargetCandidateFactory.TryCreate(
            descriptor,
            out ScrollTargetCandidate? candidate);

        Assert.IsFalse(created);
        Assert.IsNull(candidate);
    }

    [TestMethod]
    public void MatchesRequest_FiltersBySearchBoundsAndTargetHint()
    {
        ScrollTargetCandidate candidate = CreateCandidate(
            new RectD(100, 100, 400, 300),
            "Browser document",
            new Dictionary<string, string>
            {
                ["className"] = "Chrome_RenderWidgetHostHWND"
            });

        Assert.IsTrue(WindowsScrollTargetCandidateFactory.MatchesRequest(
            candidate,
            new ScrollTargetDetectionRequest(new RectD(50, 50, 200, 200), "chrome")));
        Assert.IsFalse(WindowsScrollTargetCandidateFactory.MatchesRequest(
            candidate,
            new ScrollTargetDetectionRequest(new RectD(0, 0, 20, 20), "chrome")));
        Assert.IsFalse(WindowsScrollTargetCandidateFactory.MatchesRequest(
            candidate,
            new ScrollTargetDetectionRequest(new RectD(50, 50, 200, 200), "notepad")));
    }

    private static ScrollTargetCandidate CreateCandidate(
        RectD bounds,
        string displayName,
        IReadOnlyDictionary<string, string> metadata)
    {
        return new ScrollTargetCandidate(
            "uia:42.1",
            displayName,
            ScrollTargetKind.Browser,
            bounds,
            metadata);
    }
}
