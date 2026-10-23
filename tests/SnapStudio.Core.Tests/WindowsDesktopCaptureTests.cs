using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsDesktopCaptureTests
{
    [TestMethod]
    public void DisplayChoices_UseExactFrameNumberingAndScalingWithoutScalingPhysicalBounds()
    {
        WindowsDisplayMonitor[] monitors =
        [
            new(1, new RectD(0, 0, 160, 100), true, "primary") { Dpi = 144 },
            new(2, new RectD(-80, 10, 80, 60), false, "left") { Dpi = 96 },
            new(3, new RectD(0, -40, 100, 40), false, "above") { Dpi = 192 }
        ];
        IReadOnlyList<WindowsDisplayMonitor> choices = WindowsDisplayMonitorService.NumberMonitors(monitors);
        Assert.AreSequenceEqual(new[] { "left", "above", "primary" }, choices.Select(monitor => monitor.DeviceName));
        Assert.AreEqual("Display 3 · Primary", choices[2].Title);
        Assert.AreEqual("160 × 100 · 150%", choices[2].Resolution);
        Assert.AreEqual(monitors[0].Bounds, choices[2].Bounds);
        Assert.AreEqual(new RectD(-80, -40, 240, 140), WindowsRegionCaptureGeometry.CreateVirtualScreenBounds(choices));
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(4, false)]
    public void DisplaySnapshot_DetectsMonitorMovementConnectionRemovalAndScalingChanges(int change, bool matches)
    {
        WindowsDisplayMonitor[] original = [
            new(1, new RectD(-100, 0, 100, 100), false, "left"),
            new(2, new RectD(0, 0, 100, 100), true, "primary")
        ];
        WindowsDisplayMonitor[] current = change switch
        {
            1 => [original[0] with { Bounds = new RectD(100, 0, 100, 100) }, original[1]],
            2 => [.. original, new(3, new RectD(100, 0, 100, 100), false, "new")],
            3 => [original[1]],
            4 => [original[0] with { Dpi = 144 }, original[1]],
            _ => [.. original]
        };
        Assert.AreEqual(matches, WindowsDirectCaptureGeometry.HasSameDisplayLayout(original, current));
    }

    [TestMethod]
    public void DesktopImage_PreservesNegativeOriginsVerticalOffsetsAndMixedDpiPixels()
    {
        using var workspace = TemporaryWorkspace.Create();
        WindowsDesktopCaptureImage left = CreateImage(workspace.Path, "left", Color.Red, new RectD(-4, 2, 4, 3), 144);
        WindowsDesktopCaptureImage primary = CreateImage(workspace.Path, "primary", Color.Blue, new RectD(0, 0, 6, 4), 96);
        WindowsDesktopCaptureImage top = CreateImage(workspace.Path, "top", Color.Green, new RectD(1, -2, 3, 2), 192);
        string output = Path.Combine(workspace.Path, "desktop.png");
        ImageAsset result = WindowsDesktopCaptureComposer.Save([primary, left, top], output, CancellationToken.None);
        Assert.AreEqual(10, result.Width);
        Assert.AreEqual(7, result.Height);
        using var image = new Bitmap(output);
        Assert.AreEqual(Color.Red.ToArgb(), image.GetPixel(0, 4).ToArgb());
        Assert.AreEqual(Color.Red.ToArgb(), image.GetPixel(3, 6).ToArgb());
        Assert.AreEqual(Color.Blue.ToArgb(), image.GetPixel(4, 2).ToArgb());
        Assert.AreEqual(Color.Blue.ToArgb(), image.GetPixel(9, 5).ToArgb());
        Assert.AreEqual(Color.Green.ToArgb(), image.GetPixel(5, 0).ToArgb());
        Assert.AreEqual(Color.Green.ToArgb(), image.GetPixel(7, 1).ToArgb());
        Assert.AreEqual(0, image.GetPixel(0, 0).A, "Space outside the monitors must remain transparent.");
        Assert.AreEqual(0, image.GetPixel(4, 6).A);
    }

    [TestMethod]
    public void DesktopImage_SingleMonitorKeepsNativeResolution()
    {
        using var workspace = TemporaryWorkspace.Create();
        WindowsDesktopCaptureImage monitor = CreateImage(workspace.Path, "single", Color.Coral, new RectD(200, -100, 13, 9), 192);
        string output = Path.Combine(workspace.Path, "desktop.png");
        ImageAsset result = WindowsDesktopCaptureComposer.Save([monitor], output, CancellationToken.None);
        Assert.AreEqual(13, result.Width);
        Assert.AreEqual(9, result.Height);
        using var image = new Bitmap(output);
        Assert.AreEqual(Color.Coral.ToArgb(), image.GetPixel(12, 8).ToArgb());
    }

    [TestMethod]
    public void DesktopImage_RejectsChangedResolutionInsteadOfSavingPartialOrStretchedDesktop()
    {
        using var workspace = TemporaryWorkspace.Create();
        WindowsDesktopCaptureImage monitor = CreateImage(workspace.Path, "single", Color.Red, new RectD(0, 0, 4, 3));
        string output = Path.Combine(workspace.Path, "desktop.png");
        Assert.ThrowsExactly<InvalidOperationException>(() => WindowsDesktopCaptureComposer.Save(
            [monitor with { Bounds = new RectD(0, 0, 8, 6) }], output, CancellationToken.None));
        Assert.IsFalse(File.Exists(output));
    }

    [TestMethod]
    public void DesktopImage_EmptyInvalidAndCancelledCapturesProduceNoOutput()
    {
        using var workspace = TemporaryWorkspace.Create();
        string output = Path.Combine(workspace.Path, "desktop.png");
        Assert.ThrowsExactly<ArgumentException>(() => WindowsDesktopCaptureComposer.Save([], output, CancellationToken.None));
        var invalid = new WindowsDesktopCaptureImage(new ImageAsset("unused.png", 1, 1, ImagePixelFormat.Bgra32), new RectD(double.NaN, 0, 1, 1));
        Assert.ThrowsExactly<ArgumentException>(() => WindowsDesktopCaptureComposer.Save([invalid], output, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => WindowsDesktopCaptureComposer.Save([invalid], output, cancellation.Token));
        Assert.IsFalse(File.Exists(output));
    }

    [TestMethod]
    public void WindowIdentity_RejectsReusedHandleFromAnotherProcessOrThread()
    {
        var original = new WindowsTopLevelWindow(100, "Original", new RectD(0, 0, 100, 100), "app") { ProcessId = 5, ThreadId = 6 };
        Assert.IsTrue(original.IsSameWindow(original with { Title = "Updated title" }));
        Assert.IsFalse(original.IsSameWindow(original with { ProcessId = 7 }));
        Assert.IsFalse(original.IsSameWindow(original with { ThreadId = 8 }));
        Assert.IsFalse(original.IsSameWindow(original with { Handle = 101 }));
    }

    [TestMethod]
    public async Task CancellingWindowPickerDoesNotSilentlyCaptureFrontmostWindowOrFallBack()
    {
        var picker = new CancellingPicker();
        var registry = new WindowsGraphicsCaptureItemRegistry();
        var selector = new CompositeCaptureTargetSelector([
            new WindowsDirectDisplayCaptureTargetSelector(registry, picker: picker),
            new WindowsDirectWindowCaptureTargetSelector(1, registry, picker: picker),
            new WindowsGraphicsCapturePickerTargetSelector(1, registry)
        ]);
        CaptureTargetSelection? selection = await selector.SelectTargetAsync(
            new CaptureTargetRequest([CaptureTargetKind.Window], false), CancellationToken.None);
        Assert.IsNull(selection);
        Assert.AreEqual(1, picker.WindowCalls);
        Assert.AreEqual(0, picker.DisplayCalls);
    }

    [TestMethod]
    public async Task CancellingDisplayPickerDoesNotSilentlyCapturePrimaryDisplay()
    {
        var picker = new CancellingPicker();
        var selector = new WindowsDirectDisplayCaptureTargetSelector(new WindowsGraphicsCaptureItemRegistry(), picker: picker);
        CaptureTargetSelection? selection = await selector.SelectTargetAsync(
            new CaptureTargetRequest([CaptureTargetKind.Display], false), CancellationToken.None);
        Assert.IsNull(selection);
        Assert.AreEqual(1, picker.DisplayCalls);
    }

    private static WindowsDesktopCaptureImage CreateImage(string directory, string name, Color color, RectD bounds, int dpi = 96)
    {
        string path = Path.Combine(directory, name + ".png");
        using var image = new Bitmap((int)bounds.Width, (int)bounds.Height, PixelFormat.Format32bppArgb);
        image.SetResolution(dpi, dpi);
        using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(color);
        image.Save(path, ImageFormat.Png);
        return new WindowsDesktopCaptureImage(new ImageAsset(path, image.Width, image.Height, ImagePixelFormat.Bgra32), bounds);
    }

    [TestMethod]
    public async Task EditablePointerPickerCancellationDoesNotOpenNativePickerOrCaptureAnotherTarget()
    {
        var picker = new CancellingPicker();
        var selector = new WindowsGraphicsCapturePickerTargetSelector(1, new WindowsGraphicsCaptureItemRegistry(), picker);
        CaptureTargetSelection? selection = await selector.SelectTargetAsync(new CaptureTargetRequest(
            [CaptureTargetKind.Display, CaptureTargetKind.Window], false, CaptureTargetSelectionMode.Picker, IncludeCursor: true), CancellationToken.None);
        Assert.IsNull(selection);
        Assert.AreEqual(1, picker.TargetCalls);
        Assert.AreEqual(0, picker.DisplayCalls + picker.WindowCalls);
    }

    private sealed class CancellingPicker : IWindowsCaptureTargetPicker
    {
        public int TargetCalls { get; private set; }
        public Task<WindowsCaptureTargetChoice?> PickTargetAsync(Func<IReadOnlyList<WindowsCaptureTargetChoice>> getTargets, CancellationToken cancellationToken)
        {
            TargetCalls++;
            return Task.FromResult<WindowsCaptureTargetChoice?>(null);
        }
        public int WindowCalls { get; private set; }
        public int DisplayCalls { get; private set; }

        public Task<WindowsDisplayMonitor?> PickDisplayAsync(Func<IReadOnlyList<WindowsDisplayMonitor>> getMonitors, CancellationToken cancellationToken)
        {
            DisplayCalls++;
            return Task.FromResult<WindowsDisplayMonitor?>(null);
        }

        public Task<WindowsTopLevelWindow?> PickWindowAsync(Func<IReadOnlyList<WindowsTopLevelWindow>> getWindows, CancellationToken cancellationToken)
        {
            WindowCalls++;
            return Task.FromResult<WindowsTopLevelWindow?>(null);
        }
    }
}
