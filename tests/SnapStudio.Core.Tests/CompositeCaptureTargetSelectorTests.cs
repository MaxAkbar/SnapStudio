using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

public sealed class CompositeCaptureTargetSelectorTests
{
    [Fact]
    public async Task SelectTargetAsync_ReturnsFirstSelection()
    {
        var expectedSelection = new CaptureTargetSelection(
            CaptureTargetKind.Window,
            "target",
            new RectD(0, 0, 100, 100));
        var selector = new CompositeCaptureTargetSelector(
            [
                new FakeSelector(selection: null),
                new FakeSelector(expectedSelection),
                new FakeSelector(new CaptureTargetSelection(CaptureTargetKind.Display, "later", null))
            ]);

        CaptureTargetSelection? selection = await selector.SelectTargetAsync(
            new CaptureTargetRequest([CaptureTargetKind.Window], AllowDelayedCapture: false),
            CancellationToken.None);

        Assert.Equal(expectedSelection, selection);
    }

    [Fact]
    public async Task SelectTargetAsync_WhenNoSelectorsReturnSelection_ReturnsNull()
    {
        var selector = new CompositeCaptureTargetSelector(
            [
                new FakeSelector(selection: null),
                new FakeSelector(selection: null)
            ]);

        CaptureTargetSelection? selection = await selector.SelectTargetAsync(
            new CaptureTargetRequest([CaptureTargetKind.Window], AllowDelayedCapture: false),
            CancellationToken.None);

        Assert.Null(selection);
    }

    private sealed class FakeSelector(CaptureTargetSelection? selection) : ICaptureTargetSelector
    {
        public Task<CaptureTargetSelection?> SelectTargetAsync(
            CaptureTargetRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(selection);
        }
    }
}
