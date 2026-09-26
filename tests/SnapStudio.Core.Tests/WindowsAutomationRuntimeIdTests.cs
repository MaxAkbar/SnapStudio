using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsAutomationRuntimeIdTests
{
    [TestMethod]
    public void TryGetRuntimeId_ReadsRuntimeIdFromMetadata()
    {
        ScrollTargetCandidate target = CreateTarget(
            "target",
            new Dictionary<string, string> { ["runtimeId"] = "42.1.7" });

        bool parsed = WindowsAutomationRuntimeId.TryGetRuntimeId(target, out int[] runtimeId);

        Assert.IsTrue(parsed);
        Assert.AreSequenceEqual([42, 1, 7], runtimeId);
    }

    [TestMethod]
    public void TryGetRuntimeId_ReadsRuntimeIdFromCandidateId()
    {
        ScrollTargetCandidate target = CreateTarget("uia:42.1.7", new Dictionary<string, string>());

        bool parsed = WindowsAutomationRuntimeId.TryGetRuntimeId(target, out int[] runtimeId);

        Assert.IsTrue(parsed);
        Assert.AreSequenceEqual([42, 1, 7], runtimeId);
    }

    [TestMethod]
    public void TryGetRuntimeId_WhenRuntimeIdIsInvalid_ReturnsFalse()
    {
        ScrollTargetCandidate target = CreateTarget(
            "uia:42.nope.7",
            new Dictionary<string, string>());

        bool parsed = WindowsAutomationRuntimeId.TryGetRuntimeId(target, out int[] runtimeId);

        Assert.IsFalse(parsed);
        Assert.IsEmpty(runtimeId);
    }

    private static ScrollTargetCandidate CreateTarget(
        string id,
        IReadOnlyDictionary<string, string> metadata)
    {
        return new ScrollTargetCandidate(
            id,
            "Target",
            ScrollTargetKind.Control,
            new RectD(0, 0, 100, 100),
            metadata);
    }
}
