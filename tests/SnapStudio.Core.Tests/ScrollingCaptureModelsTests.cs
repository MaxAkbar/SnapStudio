using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class ScrollingCaptureModelsTests
{
    [TestMethod]
    public void Success_CreatesSuccessfulCaptureResult()
    {
        ImageAsset image = CreateImage("stitched.png");
        ScrollingCaptureFrame frame = CreateFrame(0);

        ScrollingCaptureResult result = ScrollingCaptureResult.Success(
            image,
            [frame],
            new Dictionary<string, string> { ["frames"] = "1" });

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.HasOutput);
        Assert.IsFalse(result.IsPartial);
        Assert.IsNull(result.Failure);
        Assert.AreEqual(image, result.Image);
        Assert.ContainsSingle(result.Frames);
        Assert.AreEqual("1", result.Diagnostics["frames"]);
    }

    [TestMethod]
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

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.HasOutput);
        Assert.IsTrue(result.IsPartial);
        Assert.AreEqual(failure, result.Failure);
        Assert.AreEqual(2, result.Frames.Count);
    }

    [TestMethod]
    public void Failed_CreatesFailureWithoutOutput()
    {
        ScrollingCaptureFailure failure = new(
            ScrollingCaptureFailureReason.TargetUnavailable,
            "No scroll target was selected.");

        ScrollingCaptureResult result = ScrollingCaptureResult.Failed(failure);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.HasOutput);
        Assert.IsFalse(result.IsPartial);
        Assert.IsEmpty(result.Frames);
        Assert.AreEqual(failure, result.Failure);
    }

    [TestMethod]
    public void StitchPartial_CreatesRecoverableStitchResultWithOutput()
    {
        ImageAsset image = CreateImage("partial-stitch.png");
        ScrollingCaptureFailure failure = new(
            ScrollingCaptureFailureReason.StitchFailed,
            "Overlap was below threshold.");

        ScrollingStitchResult result = ScrollingStitchResult.Partial(image, failure);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.HasOutput);
        Assert.IsTrue(result.IsPartial);
        Assert.AreEqual(failure, result.Failure);
    }

    [TestMethod]
    public void ScrollInputResult_Success_CapturesPercentAndEndState()
    {
        ScrollInputResult result = ScrollInputResult.Success(
            reachedEnd: true,
            verticalScrollPercent: 100,
            horizontalScrollPercent: null);

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.ReachedEnd);
        Assert.AreEqual(100, result.VerticalScrollPercent);
        Assert.IsNull(result.HorizontalScrollPercent);
        Assert.IsNull(result.Failure);
    }

    [TestMethod]
    public void ScrollInputResult_Failed_CapturesTypedFailureAndLastKnownPercent()
    {
        ScrollInputFailure failure = new(
            ScrollInputFailureReason.CannotScroll,
            "Target cannot scroll down.");

        ScrollInputResult result = ScrollInputResult.Failed(
            failure,
            verticalScrollPercent: 100);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.ReachedEnd);
        Assert.AreEqual(100, result.VerticalScrollPercent);
        Assert.AreEqual(failure, result.Failure);
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
