using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Core.Tests;

public sealed class ScreenRecordingSessionCoordinatorTests
{
    [Fact]
    public async Task StartAsync_WhenRequestIsInvalid_ReturnsInvalidRequestWithoutStartingEngine()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);

        ScreenRecordingStartResult result = await coordinator.StartAsync(
            new ScreenRecordingStartRequest(CaptureTargetKind.Display, ""),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
        Assert.Equal(0, engine.StartCallCount);
    }

    [Fact]
    public async Task StartAsync_WhenRegionBoundsAreMissing_ReturnsInvalidRequest()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);

        ScreenRecordingStartResult result = await coordinator.StartAsync(
            new ScreenRecordingStartRequest(CaptureTargetKind.Region, "recording.mp4"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
        Assert.Equal(0, engine.StartCallCount);
    }

    [Fact]
    public async Task StartAsync_WhenEngineStartsSession_StoresStatus()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);

        ScreenRecordingStartResult start = await coordinator.StartAsync(
            CreateRequest(),
            CancellationToken.None);

        ScreenRecordingStatusResult status = await coordinator.GetStatusAsync(
            start.Session!.Id,
            CancellationToken.None);

        Assert.True(start.Succeeded);
        Assert.True(status.Succeeded);
        Assert.Equal(start.Session, status.Session);
        Assert.Equal(1, engine.StartCallCount);
    }

    [Fact]
    public async Task StartAsync_WhenSessionAlreadyActive_ReturnsAlreadyRecording()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);

        ScreenRecordingStartResult first = await coordinator.StartAsync(
            CreateRequest(),
            CancellationToken.None);
        ScreenRecordingStartResult second = await coordinator.StartAsync(
            CreateRequest("second.mp4"),
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.AlreadyRecording, second.Failure?.Reason);
        Assert.Equal(1, engine.StartCallCount);
    }

    [Fact]
    public async Task PauseAndResumeAsync_UpdateActiveSessionState()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);

        ScreenRecordingStartResult start = await coordinator.StartAsync(
            CreateRequest(),
            CancellationToken.None);

        ScreenRecordingControlResult pause = await coordinator.PauseAsync(
            start.Session!.Id,
            CancellationToken.None);
        ScreenRecordingControlResult resume = await coordinator.ResumeAsync(
            start.Session.Id,
            CancellationToken.None);

        Assert.True(pause.Succeeded);
        Assert.Equal(ScreenRecordingState.Paused, pause.Session?.State);
        Assert.True(resume.Succeeded);
        Assert.Equal(ScreenRecordingState.Recording, resume.Session?.State);
    }

    [Fact]
    public async Task PauseAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var coordinator = new ScreenRecordingSessionCoordinator(new FakeScreenRecordingEngine());

        ScreenRecordingControlResult result = await coordinator.PauseAsync(
            ScreenRecordingSessionId.New(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [Fact]
    public async Task StopAsync_WhenSessionIsActive_StopsEngineAndClearsStatus()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);
        ScreenRecordingStartResult start = await coordinator.StartAsync(
            CreateRequest(),
            CancellationToken.None);

        ScreenRecordingStopResult stop = await coordinator.StopAsync(
            start.Session!.Id,
            CancellationToken.None);
        ScreenRecordingStatusResult status = await coordinator.GetStatusAsync(
            start.Session.Id,
            CancellationToken.None);

        Assert.True(stop.Succeeded);
        Assert.Equal(ScreenRecordingState.Stopped, stop.Session?.State);
        Assert.Equal(ScreenRecordingState.Stopping, engine.StopSession?.State);
        Assert.False(status.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.SessionNotFound, status.Failure?.Reason);
    }

    [Fact]
    public async Task StopAsync_WhenSessionIsUnknown_ReturnsSessionNotFoundWithoutStoppingEngine()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);

        ScreenRecordingStopResult result = await coordinator.StopAsync(
            ScreenRecordingSessionId.New(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
        Assert.Equal(0, engine.StopCallCount);
    }

    private static ScreenRecordingStartRequest CreateRequest(string outputPath = "recording.mp4")
    {
        return new ScreenRecordingStartRequest(
            CaptureTargetKind.Display,
            outputPath,
            IncludeCursor: true,
            Bounds: new RectD(0, 0, 1280, 720));
    }

    private sealed class FakeScreenRecordingEngine : IScreenRecordingEngine
    {
        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public ScreenRecordingSession? StopSession { get; private set; }

        public Task<ScreenRecordingStartResult> StartAsync(
            ScreenRecordingStartRequest request,
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            var session = new ScreenRecordingSession(
                ScreenRecordingSessionId.New(),
                request.TargetKind,
                DateTimeOffset.UtcNow,
                request.OutputPath,
                ScreenRecordingState.Recording,
                new Dictionary<string, string>());

            return Task.FromResult(ScreenRecordingStartResult.Success(session));
        }

        public Task<ScreenRecordingStopResult> StopAsync(
            ScreenRecordingSession session,
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            StopSession = session;
            ScreenRecordingSession stoppedSession = session with
            {
                State = ScreenRecordingState.Stopped
            };
            var output = new ScreenRecordingOutput(
                session.OutputPath,
                TimeSpan.FromSeconds(3),
                1280,
                720,
                FileSizeBytes: 4096,
                ContainerFormat: "mp4",
                VideoCodec: "h264",
                AudioCodec: null);

            return Task.FromResult(ScreenRecordingStopResult.Success(output, stoppedSession));
        }
    }
}
