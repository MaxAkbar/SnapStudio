using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScrollingCapture;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class SystemDrawingVerticalOverlapScrollingStitcherTests
{
    [TestMethod]
    public async Task StitchAsync_WhenNoFrames_ReturnsStitchFailure()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        var stitcher = new SystemDrawingVerticalOverlapScrollingStitcher();

        ScrollingStitchResult result = await stitcher.StitchAsync(
            new ScrollingStitchRequest([], workspace.Path, new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScrollingCaptureFailureReason.StitchFailed, result.Failure?.Reason);
    }

    [TestMethod]
    public async Task StitchAsync_WhenSingleFrame_WritesFrameAsOutput()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string framePath = Path.Combine(workspace.Path, "frame-0.png");
        CreateFrame(framePath, width: 20, height: 30, y => Color.FromArgb(255, y, y, y));
        var stitcher = new SystemDrawingVerticalOverlapScrollingStitcher();

        ScrollingStitchResult result = await stitcher.StitchAsync(
            new ScrollingStitchRequest(
                [CreateCapturedFrame(0, framePath, 20, 30)],
                workspace.Path,
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Image);
        Assert.AreEqual(20, result.Image.Width);
        Assert.AreEqual(30, result.Image.Height);
        Assert.IsTrue(File.Exists(result.Image.Path));
        Assert.AreEqual("1", result.Diagnostics["frameCount"]);
    }

    [TestMethod]
    public async Task StitchAsync_WhenFramesOverlap_WritesDeduplicatedOutput()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string firstPath = Path.Combine(workspace.Path, "frame-0.png");
        string secondPath = Path.Combine(workspace.Path, "frame-1.png");
        const int width = 24;
        const int height = 40;
        const int overlap = 12;

        CreateFrame(firstPath, width, height, FirstFrameColor);
        CreateFrame(
            secondPath,
            width,
            height,
            y => y < overlap
                ? FirstFrameColor(height - overlap + y)
                : Color.FromArgb(255, 200 + y, 30 + y, 80 + y));
        var stitcher = new SystemDrawingVerticalOverlapScrollingStitcher();

        ScrollingStitchResult result = await stitcher.StitchAsync(
            new ScrollingStitchRequest(
                [
                    CreateCapturedFrame(0, firstPath, width, height),
                    CreateCapturedFrame(1, secondPath, width, height)
                ],
                workspace.Path,
                new Dictionary<string, string>
                {
                    ["minimumOverlapRatio"] = "0.1"
                }),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Image);
        Assert.AreEqual(width, result.Image.Width);
        Assert.AreEqual(height + height - overlap, result.Image.Height);
        Assert.AreEqual(overlap.ToString(), result.Diagnostics["overlap.1.rows"]);
        Assert.AreEqual("True", result.Diagnostics["overlap.1.confident"]);

        using var output = new Bitmap(result.Image.Path);
        Assert.AreEqual(FirstFrameColor(0).ToArgb(), output.GetPixel(0, 0).ToArgb());
        Assert.AreEqual(Color.FromArgb(255, 200 + overlap, 30 + overlap, 80 + overlap).ToArgb(),
            output.GetPixel(0, height).ToArgb());
    }

    [TestMethod]
    public async Task StitchAsync_WhenHeaderRepeats_CropsHeaderFromLaterFrames()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string firstPath = Path.Combine(workspace.Path, "frame-0.png");
        string secondPath = Path.Combine(workspace.Path, "frame-1.png");
        const int width = 20;
        const int height = 50;
        const int header = 8;
        const int overlap = 10;
        const int firstContentHeight = height - header;

        CreateFrame(
            firstPath,
            width,
            height,
            y => y < header ? HeaderColor(y) : StickyContentColor(y - header));
        CreateFrame(
            secondPath,
            width,
            height,
            y => y < header
                ? HeaderColor(y)
                : y < header + overlap
                    ? StickyContentColor(firstContentHeight - overlap + y - header)
                    : NewStickyContentColor(y - header - overlap));
        var stitcher = new SystemDrawingVerticalOverlapScrollingStitcher();

        ScrollingStitchResult result = await stitcher.StitchAsync(
            new ScrollingStitchRequest(
                [
                    CreateCapturedFrame(0, firstPath, width, height),
                    CreateCapturedFrame(1, secondPath, width, height)
                ],
                workspace.Path,
                new Dictionary<string, string>
                {
                    ["minimumOverlapRatio"] = "0.1"
                }),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Image);
        Assert.AreEqual(height + firstContentHeight - overlap, result.Image.Height);
        Assert.AreEqual(header.ToString(), result.Diagnostics["sticky.1.topRows"]);
        Assert.AreEqual(overlap.ToString(), result.Diagnostics["overlap.1.rows"]);
    }

    [TestMethod]
    public async Task StitchAsync_WhenFooterRepeats_CropsFooterFromEarlierFrames()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string firstPath = Path.Combine(workspace.Path, "frame-0.png");
        string secondPath = Path.Combine(workspace.Path, "frame-1.png");
        const int width = 20;
        const int height = 50;
        const int footer = 6;
        const int overlap = 10;
        const int firstContentHeight = height - footer;

        CreateFrame(
            firstPath,
            width,
            height,
            y => y >= firstContentHeight ? FooterColor(y - firstContentHeight) : StickyContentColor(y));
        CreateFrame(
            secondPath,
            width,
            height,
            y => y >= firstContentHeight
                ? FooterColor(y - firstContentHeight)
                : y < overlap
                    ? StickyContentColor(firstContentHeight - overlap + y)
                    : NewStickyContentColor(y - overlap));
        var stitcher = new SystemDrawingVerticalOverlapScrollingStitcher();

        ScrollingStitchResult result = await stitcher.StitchAsync(
            new ScrollingStitchRequest(
                [
                    CreateCapturedFrame(0, firstPath, width, height),
                    CreateCapturedFrame(1, secondPath, width, height)
                ],
                workspace.Path,
                new Dictionary<string, string>
                {
                    ["minimumOverlapRatio"] = "0.1"
                }),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Image);
        Assert.AreEqual(firstContentHeight + height - overlap, result.Image.Height);
        Assert.AreEqual(footer.ToString(), result.Diagnostics["sticky.0.bottomRows"]);
        Assert.AreEqual("0", result.Diagnostics["sticky.1.bottomRows"]);
        Assert.AreEqual(overlap.ToString(), result.Diagnostics["overlap.1.rows"]);
    }

    [TestMethod]
    public async Task StitchAsync_WhenOverlapIsNotConfident_StacksFrames()
    {
        using TemporaryWorkspace workspace = TemporaryWorkspace.Create();
        string firstPath = Path.Combine(workspace.Path, "frame-0.png");
        string secondPath = Path.Combine(workspace.Path, "frame-1.png");
        CreateFrame(firstPath, width: 16, height: 20, y => Color.FromArgb(255, 220, y, y));
        CreateFrame(secondPath, width: 16, height: 20, y => Color.FromArgb(255, y, 30, 220));
        var stitcher = new SystemDrawingVerticalOverlapScrollingStitcher();

        ScrollingStitchResult result = await stitcher.StitchAsync(
            new ScrollingStitchRequest(
                [
                    CreateCapturedFrame(0, firstPath, 16, 20),
                    CreateCapturedFrame(1, secondPath, 16, 20)
                ],
                workspace.Path,
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Image);
        Assert.AreEqual(40, result.Image.Height);
        Assert.AreEqual("0", result.Diagnostics["overlap.1.rows"]);
        Assert.AreEqual("False", result.Diagnostics["overlap.1.confident"]);
    }

    private static ScrollingCaptureFrame CreateCapturedFrame(
        int index,
        string path,
        int width,
        int height)
    {
        return new ScrollingCaptureFrame(
            index,
            new ImageAsset(path, width, height, ImagePixelFormat.Bgra32),
            new RectD(0, 0, width, height),
            index,
            new Dictionary<string, string>());
    }

    private static void CreateFrame(
        string path,
        int width,
        int height,
        Func<int, Color> colorForRow)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < height; y++)
        {
            Color color = colorForRow(y);
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, color);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static Color FirstFrameColor(int y)
    {
        return Color.FromArgb(255, y * 3, 80 + y, 160 - y);
    }

    private static Color HeaderColor(int y)
    {
        return Color.FromArgb(255, 30 + y, 40 + y, 50 + y);
    }

    private static Color FooterColor(int y)
    {
        return Color.FromArgb(255, 90 + y, 110 + y, 130 + y);
    }

    private static Color StickyContentColor(int y)
    {
        return Color.FromArgb(255, 40 + y * 3, 30 + y * 2, 210 - y);
    }

    private static Color NewStickyContentColor(int y)
    {
        return Color.FromArgb(255, 180 - y, 60 + y * 2, 40 + y);
    }
}
