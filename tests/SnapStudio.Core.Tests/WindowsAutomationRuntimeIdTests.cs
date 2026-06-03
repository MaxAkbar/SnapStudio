using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

public sealed class WindowsAutomationRuntimeIdTests
{
    [Fact]
    public void TryGetRuntimeId_ReadsRuntimeIdFromMetadata()
    {
        ScrollTargetCandidate target = CreateTarget(
            "target",
            new Dictionary<string, string> { ["runtimeId"] = "42.1.7" });

        bool parsed = WindowsAutomationRuntimeId.TryGetRuntimeId(target, out int[] runtimeId);

        Assert.True(parsed);
        Assert.Equal([42, 1, 7], runtimeId);
    }

    [Fact]
    public void TryGetRuntimeId_ReadsRuntimeIdFromCandidateId()
    {
        ScrollTargetCandidate target = CreateTarget("uia:42.1.7", new Dictionary<string, string>());

        bool parsed = WindowsAutomationRuntimeId.TryGetRuntimeId(target, out int[] runtimeId);

        Assert.True(parsed);
        Assert.Equal([42, 1, 7], runtimeId);
    }

    [Fact]
    public void TryGetRuntimeId_WhenRuntimeIdIsInvalid_ReturnsFalse()
    {
        ScrollTargetCandidate target = CreateTarget(
            "uia:42.nope.7",
            new Dictionary<string, string>());

        bool parsed = WindowsAutomationRuntimeId.TryGetRuntimeId(target, out int[] runtimeId);

        Assert.False(parsed);
        Assert.Empty(runtimeId);
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
