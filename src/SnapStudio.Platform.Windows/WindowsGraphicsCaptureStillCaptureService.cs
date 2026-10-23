using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.System;
using Microsoft.Graphics.Canvas;
using System.Globalization;
using System.Collections.Concurrent;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsGraphicsCaptureStillCaptureService : IStillCaptureService
{
    private readonly IStillCaptureCapabilityService _capabilities;
    private readonly IClock _clock;
    private readonly string _outputDirectory;
    private readonly WindowsGraphicsCaptureItemRegistry? _registry;

    public WindowsGraphicsCaptureStillCaptureService(
        WindowsGraphicsCaptureItemRegistry? registry = null,
        string? outputDirectory = null,
        IStillCaptureCapabilityService? capabilities = null,
        IClock? clock = null)
    {
        if (outputDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        }

        _registry = registry;
        _outputDirectory = Path.GetFullPath(outputDirectory ?? Path.Combine(
            Path.GetTempPath(),
            "SnapStudio",
            "Captures"));
        _capabilities = capabilities ?? new WindowsGraphicsCaptureCapabilityService();
        _clock = clock ?? new SystemClock();
    }

    public async Task<CaptureOutcome> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        StillCaptureCapability capability = await _capabilities
            .GetCapabilityAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!capability.IsSupported)
        {
            return CaptureOutcome.Failed(new CaptureFailure(
                CaptureFailureReason.Unsupported,
                capability.UnavailableReason ?? "Windows.Graphics.Capture is unavailable."));
        }

        if (!capability.SupportedTargets.Contains(request.TargetKind))
        {
            return CaptureOutcome.Failed(new CaptureFailure(
                CaptureFailureReason.Unsupported,
                $"Capture target '{request.TargetKind}' is not supported by the Windows.Graphics.Capture adapter."));
        }

        GraphicsCaptureItem? item = null;
        IReadOnlyList<WindowsDesktopCaptureTarget>? desktop = null;
        if (!string.IsNullOrWhiteSpace(request.TargetHint) && _registry is not null)
        {
            if (request.TargetKind == CaptureTargetKind.FullScreen && _registry.TryGetDesktop(request.TargetHint, out var targets))
                desktop = targets;
            else if (_registry.TryGet(request.TargetHint, out GraphicsCaptureItem selected))
                item = selected;
        }

        if (item is null && desktop is null)
        {
            return CaptureOutcome.Failed(new CaptureFailure(
                CaptureFailureReason.TargetUnavailable,
                "No Windows.Graphics.Capture target was selected."));
        }

        WindowsCaptureTargetGeometry? geometry = null;
        if (item is not null && request.TargetHint is not null)
            _registry?.TryGetGeometry(request.TargetHint, out geometry);
        if (request.IncludeCursor && (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            || (desktop is null && geometry is null)))
            return CaptureOutcome.Failed(new CaptureFailure(CaptureFailureReason.Unsupported,
                "Choose a display or window again to capture an editable mouse pointer. Windows 10 version 2004 or later is required."));

        if (request.Delay > TimeSpan.Zero)
        {
            await Task.Delay(request.Delay, cancellationToken).ConfigureAwait(false);
        }

        if (desktop is not null && !WindowsDirectCaptureGeometry.HasSameDisplayLayout(
                desktop.Select(target => target.Monitor), new WindowsDisplayMonitorService().GetMonitors()))
        {
            return CaptureOutcome.Failed(new CaptureFailure(CaptureFailureReason.TargetUnavailable,
                "The display configuration changed. Start the capture again."));
        }

        if ((item is not null && !HasValidCaptureItemSize(item.Size))
            || (desktop is not null && desktop.Any(target => !HasValidCaptureItemSize(target.Item.Size))))
        {
            return CaptureOutcome.Failed(new CaptureFailure(
                CaptureFailureReason.TargetUnavailable,
                "The selected window is minimized or unavailable. Restore the window and try the capture again."));
        }

        try
        {
            CaptureResult capture = desktop is not null
                ? await CaptureDesktopAsync(desktop, request, cancellationToken).ConfigureAwait(false)
                : await CaptureFrameAsync(item!, request, cancellationToken, geometry).ConfigureAwait(false);

            return CaptureOutcome.Success(capture);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CaptureOutcome.Failed(new CaptureFailure(
                CaptureFailureReason.TargetUnavailable,
                "The selected target did not produce a capture frame. If the window is minimized, restore it and try again."));
        }
        catch (Exception exception)
        {
            return CaptureOutcome.Failed(new CaptureFailure(
                CaptureFailureReason.Unknown,
                "Windows.Graphics.Capture failed while acquiring the still frame.",
                exception));
        }
    }

    private static bool HasValidCaptureItemSize(global::Windows.Graphics.SizeInt32 size)
    {
        return size.Width > 0 && size.Height > 0;
    }

    private async Task<CaptureResult> CaptureDesktopAsync(
        IReadOnlyList<WindowsDesktopCaptureTarget> targets,
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        var captures = new ConcurrentBag<CaptureResult>();
        CaptureId id = CaptureId.New();
        string outputPath = Path.Combine(_outputDirectory, $"{id}.png");
        bool saved = false;
        WindowsCursorSnapshot? cursor = null;
        int cursorSampled = 0;
        void SampleCursor()
        {
            if (request.IncludeCursor && Interlocked.CompareExchange(ref cursorSampled, 1, 0) == 0)
                cursor = WindowsCursorCapture.Capture();
        }
        try
        {
            WindowsDesktopCaptureImage[] images = await Task.WhenAll(targets.Select(async target =>
            {
                CaptureResult capture = await CaptureFrameAsync(target.Item, request with
                {
                    TargetKind = CaptureTargetKind.Display,
                    IncludeCursor = false,
                    Delay = TimeSpan.Zero,
                    Bounds = null,
                    TargetMetadata = null
                }, cancellationToken, frameCaptured: SampleCursor).ConfigureAwait(false);
                captures.Add(capture);
                return new WindowsDesktopCaptureImage(capture.SourceImage, target.Monitor.Bounds);
            })).ConfigureAwait(false);

            ImageAsset image = await Task.Run(() => WindowsDesktopCaptureComposer.Save(images, outputPath, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = request.TargetMetadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(request.TargetMetadata);
            metadata["source"] = "capture";
            metadata["targetKind"] = CaptureTargetKind.FullScreen.ToString();
            metadata["displayName"] = "All displays";
            metadata["monitorCount"] = targets.Count.ToString(CultureInfo.InvariantCulture);
            metadata["includeCursor"] = request.IncludeCursor.ToString();
            metadata["delayMilliseconds"] = request.Delay.TotalMilliseconds.ToString(CultureInfo.InvariantCulture);
            saved = true;
            return new CaptureResult(id, _clock.UtcNow, image, metadata,
                cursor?.ForCapture(WindowsRegionCaptureGeometry.CreateVirtualScreenBounds(targets.Select(target => target.Monitor).ToArray())));
        }
        finally
        {
            foreach (CaptureResult capture in captures) TryDeleteCapture(capture.SourceImage.Path);
            if (!saved) TryDeleteCapture(outputPath);
        }
    }

    private static void TryDeleteCapture(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<CaptureResult> CaptureFrameAsync(
        GraphicsCaptureItem item,
        CaptureRequest request,
        CancellationToken cancellationToken,
        WindowsCaptureTargetGeometry? geometry = null,
        Action? frameCaptured = null)
    {
        Directory.CreateDirectory(_outputDirectory);

        CaptureId captureId = CaptureId.New();
        string outputPath = Path.Combine(_outputDirectory, $"{captureId}.png");

        CanvasDevice canvasDevice = CanvasDevice.GetSharedDevice();
        using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            canvasDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            session.IsCursorCaptureEnabled = false;
        }

        using Direct3D11CaptureFrame frame = await CaptureOneFrameAsync(
                framePool,
                session,
                cancellationToken)
            .ConfigureAwait(false);
        frameCaptured?.Invoke();
        using CanvasBitmap bitmap = CanvasBitmap.CreateFromDirect3D11Surface(
            canvasDevice,
            frame.Surface);

        RectD outputBounds = ResolveOutputBounds(frame.ContentSize, request);
        CapturedCursor? cursor = null;
        if (request.IncludeCursor && geometry is not null)
        {
            RectD screen = geometry.WindowHandle == 0 ? geometry.ScreenBounds
                : WindowsTopLevelWindowService.GetVisibleBounds(geometry.WindowHandle) ?? geometry.ScreenBounds;
            cursor = WindowsCursorCapture.Capture()?.ForCapture(new RectD(screen.X + outputBounds.X,
                screen.Y + outputBounds.Y, outputBounds.Width, outputBounds.Height), geometry.WindowHandle);
        }
        if (request.TargetKind == CaptureTargetKind.Region)
        {
            using CanvasRenderTarget croppedBitmap = CreateCroppedBitmap(
                canvasDevice,
                bitmap,
                outputBounds);

            await croppedBitmap.SaveAsync(outputPath).AsTask(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await bitmap.SaveAsync(outputPath).AsTask(cancellationToken).ConfigureAwait(false);
        }

        var metadata = new Dictionary<string, string>
        {
            ["source"] = "capture",
            ["targetKind"] = request.TargetKind.ToString(),
            ["targetHint"] = request.TargetHint ?? string.Empty,
            ["includeCursor"] = request.IncludeCursor.ToString(),
            ["delayMilliseconds"] = request.Delay.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            ["displayName"] = item.DisplayName,
            ["boundsX"] = outputBounds.X.ToString(CultureInfo.InvariantCulture),
            ["boundsY"] = outputBounds.Y.ToString(CultureInfo.InvariantCulture),
            ["boundsWidth"] = outputBounds.Width.ToString(CultureInfo.InvariantCulture),
            ["boundsHeight"] = outputBounds.Height.ToString(CultureInfo.InvariantCulture)
        };

        if (request.TargetMetadata is not null)
        {
            foreach ((string key, string value) in request.TargetMetadata)
            {
                metadata[key] = value;
            }
        }

        return new CaptureResult(
            captureId,
            _clock.UtcNow,
            new ImageAsset(
                outputPath,
                (int)Math.Round(outputBounds.Width),
                (int)Math.Round(outputBounds.Height),
                ImagePixelFormat.Bgra32),
            metadata,
            cursor);
    }

    private static CanvasRenderTarget CreateCroppedBitmap(
        CanvasDevice canvasDevice,
        CanvasBitmap source,
        RectD outputBounds)
    {
        var renderTarget = new CanvasRenderTarget(
            canvasDevice,
            (float)outputBounds.Width,
            (float)outputBounds.Height,
            source.Dpi);

        using CanvasDrawingSession drawingSession = renderTarget.CreateDrawingSession();
        drawingSession.Clear(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
        drawingSession.DrawImage(
            source,
            new Rect(0, 0, outputBounds.Width, outputBounds.Height),
            new Rect(outputBounds.X, outputBounds.Y, outputBounds.Width, outputBounds.Height));

        return renderTarget;
    }

    private static RectD ResolveOutputBounds(
        global::Windows.Graphics.SizeInt32 contentSize,
        CaptureRequest request)
    {
        if (request.TargetKind != CaptureTargetKind.Region || request.Bounds is null)
        {
            return new RectD(0, 0, contentSize.Width, contentSize.Height);
        }

        RectD normalized = NormalizeBounds(request.Bounds.Value);
        double left = Math.Clamp(normalized.X, 0, contentSize.Width);
        double top = Math.Clamp(normalized.Y, 0, contentSize.Height);
        double right = Math.Clamp(normalized.Right, left, contentSize.Width);
        double bottom = Math.Clamp(normalized.Bottom, top, contentSize.Height);

        return new RectD(
            left,
            top,
            Math.Max(1, right - left),
            Math.Max(1, bottom - top));
    }

    private static RectD NormalizeBounds(RectD bounds)
    {
        double x = Math.Min(bounds.X, bounds.Right);
        double y = Math.Min(bounds.Y, bounds.Bottom);
        double right = Math.Max(bounds.X, bounds.Right);
        double bottom = Math.Max(bounds.Y, bounds.Bottom);

        return new RectD(x, y, right - x, bottom - y);
    }

    private static async Task<Direct3D11CaptureFrame> CaptureOneFrameAsync(
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession session,
        CancellationToken cancellationToken)
    {
        var frameReceived = new TaskCompletionSource<Direct3D11CaptureFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
            if (frame is not null && !frameReceived.TrySetResult(frame))
            {
                frame.Dispose();
            }
        }

        framePool.FrameArrived += OnFrameArrived;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            await using CancellationTokenRegistration registration = timeout.Token.Register(
                () => frameReceived.TrySetCanceled(timeout.Token));

            session.StartCapture();

            return await frameReceived.Task.ConfigureAwait(false);
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
        }
    }
}
