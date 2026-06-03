using System.Collections.Concurrent;
using System.Globalization;
using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScreenRecording;
using SnapStudio.Core.System;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsGraphicsCaptureScreenRecordingFrameSource : IScreenRecordingFrameSource
{
    private readonly IStillCaptureCapabilityService _capabilities;
    private readonly IClock _clock;
    private readonly WindowsGraphicsCaptureItemRegistry? _registry;
    private readonly ConcurrentDictionary<ScreenRecordingSessionId, FrameSourceRuntime> _sessions = new();

    public WindowsGraphicsCaptureScreenRecordingFrameSource(
        WindowsGraphicsCaptureItemRegistry? registry = null,
        IStillCaptureCapabilityService? capabilities = null,
        IClock? clock = null)
    {
        _registry = registry;
        _capabilities = capabilities ?? new WindowsGraphicsCaptureCapabilityService();
        _clock = clock ?? new SystemClock();
    }

    public async Task<ScreenRecordingFrameSourceOpenResult> OpenAsync(
        ScreenRecordingFrameSourceOpenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RecordingRequest);
        cancellationToken.ThrowIfCancellationRequested();

        StillCaptureCapability capability = await _capabilities
            .GetCapabilityAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!capability.IsSupported)
        {
            return ScreenRecordingFrameSourceOpenResult.Failed(new ScreenRecordingFailure(
                ScreenRecordingFailureReason.Unsupported,
                capability.UnavailableReason ?? "Windows.Graphics.Capture is unavailable."));
        }

        ScreenRecordingStartRequest recordingRequest = request.RecordingRequest;
        if (!capability.SupportedTargets.Contains(recordingRequest.TargetKind))
        {
            return ScreenRecordingFrameSourceOpenResult.Failed(new ScreenRecordingFailure(
                ScreenRecordingFailureReason.Unsupported,
                $"Recording target '{recordingRequest.TargetKind}' is not supported by Windows.Graphics.Capture."));
        }

        if (string.IsNullOrWhiteSpace(recordingRequest.TargetHint)
            || _registry is null
            || !_registry.TryGet(recordingRequest.TargetHint, out GraphicsCaptureItem item))
        {
            return ScreenRecordingFrameSourceOpenResult.Failed(new ScreenRecordingFailure(
                ScreenRecordingFailureReason.TargetUnavailable,
                "No Windows.Graphics.Capture recording target was selected."));
        }

        try
        {
            CanvasDevice canvasDevice = CanvasDevice.GetSharedDevice();
            Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                canvasDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);
            GraphicsCaptureSession session = framePool.CreateCaptureSession(item);

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                session.IsCursorCaptureEnabled = recordingRequest.IncludeCursor;
            }

            RectD outputBounds = ResolveOutputBounds(item.Size, recordingRequest);
            var runtime = new FrameSourceRuntime(
                request.SessionId,
                framePool,
                session,
                item.Size.Width,
                item.Size.Height,
                outputBounds,
                _clock.UtcNow);

            if (!_sessions.TryAdd(request.SessionId, runtime))
            {
                runtime.Dispose();

                return ScreenRecordingFrameSourceOpenResult.Failed(new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.AlreadyRecording,
                    "A recording frame source session with the same id is already active."));
            }

            return ScreenRecordingFrameSourceOpenResult.Success(
                new ScreenRecordingFrameSourceSession(
                    request.SessionId,
                    (int)Math.Round(outputBounds.Width),
                    (int)Math.Round(outputBounds.Height),
                    DirectXPixelFormat.B8G8R8A8UIntNormalized.ToString(),
                    FrameInterval: null,
                    CreateMetadata(item, recordingRequest, outputBounds)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ScreenRecordingFrameSourceOpenResult.Failed(new ScreenRecordingFailure(
                ScreenRecordingFailureReason.Unknown,
                "Windows.Graphics.Capture failed while opening the recording frame source.",
                exception));
        }
    }

    public Task<ScreenRecordingFrameSourceStartResult> StartAsync(
        ScreenRecordingFrameSourceStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FrameSink);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(request.SessionId, out FrameSourceRuntime? runtime))
        {
            return Task.FromResult(ScreenRecordingFrameSourceStartResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.SessionNotFound,
                    "The Windows.Graphics.Capture frame source session was not found.")));
        }

        try
        {
            runtime.Start(request.FrameSink);

            return Task.FromResult(ScreenRecordingFrameSourceStartResult.Success(
                new Dictionary<string, string>
                {
                    ["sourceStartedAtUtc"] = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                }));
        }
        catch (Exception exception)
        {
            _sessions.TryRemove(request.SessionId, out _);
            runtime.Dispose();

            return Task.FromResult(ScreenRecordingFrameSourceStartResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.Unknown,
                    "Windows.Graphics.Capture failed while starting the recording frame source.",
                    exception)));
        }
    }

    public Task<ScreenRecordingFrameSourceStopResult> StopAsync(
        ScreenRecordingSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryRemove(sessionId, out FrameSourceRuntime? runtime))
        {
            return Task.FromResult(ScreenRecordingFrameSourceStopResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.SessionNotFound,
                    "The Windows.Graphics.Capture frame source session was not found.")));
        }

        try
        {
            runtime.Dispose();

            return Task.FromResult(ScreenRecordingFrameSourceStopResult.Success(
                new Dictionary<string, string>
                {
                    ["framesReceived"] = runtime.FramesReceived.ToString(CultureInfo.InvariantCulture),
                    ["framesAccepted"] = runtime.FramesAccepted.ToString(CultureInfo.InvariantCulture),
                    ["frameWriteFailures"] = runtime.FrameWriteFailures.ToString(CultureInfo.InvariantCulture),
                    ["sourceContentWidth"] = runtime.ContentWidth.ToString(CultureInfo.InvariantCulture),
                    ["sourceContentHeight"] = runtime.ContentHeight.ToString(CultureInfo.InvariantCulture),
                    ["startedAtUtc"] = runtime.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    ["stoppedAtUtc"] = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                }));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ScreenRecordingFrameSourceStopResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.Unknown,
                    "Windows.Graphics.Capture failed while stopping the recording frame source.",
                    exception)));
        }
    }

    private static Dictionary<string, string> CreateMetadata(
        GraphicsCaptureItem item,
        ScreenRecordingStartRequest request,
        RectD outputBounds)
    {
        var metadata = new Dictionary<string, string>
        {
            ["adapter"] = "WindowsGraphicsCapture",
            ["targetKind"] = request.TargetKind.ToString(),
            ["targetHint"] = request.TargetHint ?? string.Empty,
            ["includeCursor"] = request.IncludeCursor.ToString(),
            ["displayName"] = item.DisplayName,
            ["sourceContentWidth"] = item.Size.Width.ToString(CultureInfo.InvariantCulture),
            ["sourceContentHeight"] = item.Size.Height.ToString(CultureInfo.InvariantCulture),
            ["boundsX"] = outputBounds.X.ToString(CultureInfo.InvariantCulture),
            ["boundsY"] = outputBounds.Y.ToString(CultureInfo.InvariantCulture),
            ["boundsWidth"] = outputBounds.Width.ToString(CultureInfo.InvariantCulture),
            ["boundsHeight"] = outputBounds.Height.ToString(CultureInfo.InvariantCulture)
        };

        if (request.TargetMetadata is not null)
        {
            foreach ((string key, string value) in request.TargetMetadata)
            {
                metadata[$"target:{key}"] = value;
            }
        }

        return metadata;
    }

    private static RectD ResolveOutputBounds(
        global::Windows.Graphics.SizeInt32 contentSize,
        ScreenRecordingStartRequest request)
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

    private sealed class FrameSourceRuntime : IDisposable
    {
        private readonly ScreenRecordingSessionId _sessionId;
        private readonly Direct3D11CaptureFramePool _framePool;
        private readonly GraphicsCaptureSession _session;
        private readonly RectD _outputBounds;
        private int _disposed;
        private int _frameWriteFailures;
        private int _framesAccepted;
        private int _framesReceived;
        private int _started;
        private IScreenRecordingFrameSink? _frameSink;

        public FrameSourceRuntime(
            ScreenRecordingSessionId sessionId,
            Direct3D11CaptureFramePool framePool,
            GraphicsCaptureSession session,
            int contentWidth,
            int contentHeight,
            RectD outputBounds,
            DateTimeOffset startedAtUtc)
        {
            _sessionId = sessionId;
            _framePool = framePool;
            _session = session;
            ContentWidth = contentWidth;
            ContentHeight = contentHeight;
            _outputBounds = outputBounds;
            StartedAtUtc = startedAtUtc;
        }

        public int FramesReceived => Volatile.Read(ref _framesReceived);

        public int FramesAccepted => Volatile.Read(ref _framesAccepted);

        public int FrameWriteFailures => Volatile.Read(ref _frameWriteFailures);

        public int ContentWidth { get; }

        public int ContentHeight { get; }

        public DateTimeOffset StartedAtUtc { get; }

        public void Start(IScreenRecordingFrameSink frameSink)
        {
            ArgumentNullException.ThrowIfNull(frameSink);

            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                throw new InvalidOperationException("The Windows.Graphics.Capture frame source already started.");
            }

            _frameSink = frameSink;
            _framePool.FrameArrived += OnFrameArrived;
            try
            {
                _session.StartCapture();
            }
            catch
            {
                _framePool.FrameArrived -= OnFrameArrived;
                _frameSink = null;
                throw;
            }
        }

        public void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            IScreenRecordingFrameSink? frameSink = _frameSink;
            if (frameSink is null || Volatile.Read(ref _disposed) == 1)
            {
                frame.Dispose();
                return;
            }

            long sequenceNumber = Interlocked.Increment(ref _framesReceived);
            var recordingFrame = new WindowsGraphicsCaptureRecordingFrame(
                _sessionId,
                sequenceNumber,
                frame.SystemRelativeTime,
                (int)Math.Round(_outputBounds.Width),
                (int)Math.Round(_outputBounds.Height),
                DirectXPixelFormat.B8G8R8A8UIntNormalized.ToString(),
                new Dictionary<string, string>
                {
                    ["contentWidth"] = frame.ContentSize.Width.ToString(CultureInfo.InvariantCulture),
                    ["contentHeight"] = frame.ContentSize.Height.ToString(CultureInfo.InvariantCulture),
                    ["outputBoundsX"] = _outputBounds.X.ToString(CultureInfo.InvariantCulture),
                    ["outputBoundsY"] = _outputBounds.Y.ToString(CultureInfo.InvariantCulture),
                    ["outputBoundsWidth"] = _outputBounds.Width.ToString(CultureInfo.InvariantCulture),
                    ["outputBoundsHeight"] = _outputBounds.Height.ToString(CultureInfo.InvariantCulture)
                },
                frame);

            _ = DeliverFrameAsync(frameSink, recordingFrame);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _framePool.FrameArrived -= OnFrameArrived;
            _session.Dispose();
            _framePool.Dispose();
        }

        private async Task DeliverFrameAsync(
            IScreenRecordingFrameSink frameSink,
            WindowsGraphicsCaptureRecordingFrame frame)
        {
            try
            {
                ScreenRecordingFrameWriteResult result = await frameSink
                    .WriteFrameAsync(frame, CancellationToken.None)
                    .ConfigureAwait(false);

                if (result.Succeeded)
                {
                    Interlocked.Increment(ref _framesAccepted);
                }
                else
                {
                    Interlocked.Increment(ref _frameWriteFailures);
                }
            }
            catch
            {
                Interlocked.Increment(ref _frameWriteFailures);
            }
            finally
            {
                frame.Dispose();
            }
        }
    }

    internal sealed class WindowsGraphicsCaptureRecordingFrame : IWindowsScreenRecordingPixelFrame
    {
        private int _disposed;

        public WindowsGraphicsCaptureRecordingFrame(
            ScreenRecordingSessionId sessionId,
            long sequenceNumber,
            TimeSpan timestamp,
            int width,
            int height,
            string pixelFormat,
            IReadOnlyDictionary<string, string> metadata,
            Direct3D11CaptureFrame captureFrame)
        {
            ArgumentNullException.ThrowIfNull(captureFrame);

            SessionId = sessionId;
            SequenceNumber = sequenceNumber;
            Timestamp = timestamp;
            Width = width;
            Height = height;
            PixelFormat = pixelFormat;
            Metadata = metadata;
            CaptureFrame = captureFrame;
        }

        public ScreenRecordingSessionId SessionId { get; }

        public long SequenceNumber { get; }

        public TimeSpan Timestamp { get; }

        public int Width { get; }

        public int Height { get; }

        public string PixelFormat { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        public Direct3D11CaptureFrame CaptureFrame { get; }

        public byte[] CopyPixelBytes()
        {
            using CanvasBitmap bitmap = CanvasBitmap.CreateFromDirect3D11Surface(
                CanvasDevice.GetSharedDevice(),
                CaptureFrame.Surface);

            return bitmap.GetPixelBytes();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            CaptureFrame.Dispose();
        }
    }
}
