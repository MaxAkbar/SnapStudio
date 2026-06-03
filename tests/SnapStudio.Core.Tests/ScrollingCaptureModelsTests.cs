using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Core.Tests;

public sealed class ScrollingCaptureModelsTests
{
    [Fact]
    public void Success_CreatesSuccessfulCaptureResult()
    {
        ImageAsset image = CreateImage("stitched.png");
        ScrollingCaptureFrame frame = CreateFrame(0);

        ScrollingCaptureResult result = ScrollingCaptureResult.Success(
            image,
            [frame],
            new Dictionary<string, string> { ["frames"] = "1" });

        Assert.True(result.Succeeded);
        Assert.True(result.HasOutput);
        Assert.False(result.IsPartial);
        Assert.Null(result.Failure);
        Assert.Equal(image, result.Image);
        Assert.Single(result.Frames);
        Assert.Equal("1", result.Diagnostics["frames"]);
    }

    [Fact]
    public void Partial_CreatesRecoverableCaptureResultWithOutput()
    {
        ImageAsset image = CreateImage("partial.png");
        ScrollingCaptureFailure failure = new(
            ScrollingCaptureFailureReason.StitchFailed,
            "Stitch stopped at sticky footer.");

        ScrollingCaptureResult result = ScrollingCaptureResult.Partial(
            image,
            [CreateFrame(0), CreateFrame(1)],
            failure);

        Assert.False(result.Succeeded);
        Assert.True(result.HasOutput);
        Assert.True(result.IsPartial);
        Assert.Equal(failure, result.Failure);
        Assert.Equal(2, result.Frames.Count);
    }

    [Fact]
    public void Failed_CreatesFailureWithoutOutput()
    {
        ScrollingCaptureFailure failure = new(
            ScrollingCaptureFailureReason.TargetUnavailable,
            "No scroll target was selected.");

        ScrollingCaptureResult result = ScrollingCaptureResult.Failed(failure);

        Assert.False(result.Succeeded);
        Assert.False(result.HasOutput);
        Assert.False(result.IsPartial);
        Assert.Empty(result.Frames);
        Assert.Equal(failure, result.Failure);
    }

    [Fact]
    public void StitchPartial_CreatesRecoverableStitchResultWithOutput()
    {
        ImageAsset image = CreateImage("partial-stitch.png");
        ScrollingCaptureFailure failure = new(
            ScrollingCaptureFailureReason.StitchFailed,
            "Overlap was below threshold.");

        ScrollingStitchResult result = ScrollingStitchResult.Partial(image, failure);

        Assert.False(result.Succeeded);
        Assert.True(result.HasOutput);
        Assert.True(result.IsPartial);
        Assert.Equal(failure, result.Failure);
    }

    [Fact]
    public void ScrollInputResult_Success_CapturesPercentAndEndState()
    {
        ScrollInputResult result = ScrollInputResult.Success(
            reachedEnd: true,
            verticalScrollPercent: 100,
            horizontalScrollPercent: null);

        Assert.True(result.Succeeded);
        Assert.True(result.ReachedEnd);
        Assert.Equal(100, result.VerticalScrollPercent);
        Assert.Null(result.HorizontalScrollPercent);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void ScrollInputResult_Failed_CapturesTypedFailureAndLastKnownPercent()
    {
        ScrollInputFailure failure = new(
            ScrollInputFailureReason.CannotScroll,
            "Target cannot scroll down.");

        ScrollInputResult result = ScrollInputResult.Failed(
            failure,
            verticalScrollPercent: 100);

        Assert.False(result.Succeeded);
        Assert.False(result.ReachedEnd);
        Assert.Equal(100, result.VerticalScrollPercent);
        Assert.Equal(failure, result.Failure);
    }

    private static ScrollingCaptureFrame CreateFrame(int index)
    {
        return new ScrollingCaptureFrame(
            index,
            CreateImage($"frame-{index}.png"),
            new RectD(0, index * 100, 800, 600),
            index * 100,
            new Dictionary<string, string>());
    }

    private static ImageAsset CreateImage(string path)
    {
        return new ImageAsset(path, 800, 600, ImagePixelFormat.Bgra32);
    }
}
