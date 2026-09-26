using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.ScreenRecording;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class ScreenRecordingSessionCoordinatorTests
{
    [TestMethod]
    public async Task StartAsync_WhenRequestIsInvalid_ReturnsInvalidRequestWithoutStartingEngine()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);

        ScreenRecordingStartResult result = await coordinator.StartAsync(
            new ScreenRecordingStartRequest(CaptureTargetKind.Display, ""),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
        Assert.AreEqual(0, engine.StartCallCount);
    }

    [TestMethod]
    public async Task StartAsync_WhenRegionBoundsAreMissing_ReturnsInvalidRequest()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);

        ScreenRecordingStartResult result = await coordinator.StartAsync(
            new ScreenRecordingStartRequest(CaptureTargetKind.Region, "recording.mp4"),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.InvalidRequest, result.Failure?.Reason);
        Assert.AreEqual(0, engine.StartCallCount);
    }

    [TestMethod]
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

        Assert.IsTrue(start.Succeeded);
        Assert.IsTrue(status.Succeeded);
        Assert.AreEqual(start.Session, status.Session);
        Assert.AreEqual(1, engine.StartCallCount);
    }

    [TestMethod]
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

        Assert.IsTrue(first.Succeeded);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.AlreadyRecording, second.Failure?.Reason);
        Assert.AreEqual(1, engine.StartCallCount);
    }

    [TestMethod]
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

        Assert.IsTrue(pause.Succeeded);
        Assert.AreEqual(ScreenRecordingState.Paused, pause.Session?.State);
        Assert.IsTrue(resume.Succeeded);
        Assert.AreEqual(ScreenRecordingState.Recording, resume.Session?.State);
    }

    [TestMethod]
    public async Task PauseAsync_WhenSessionIsUnknown_ReturnsSessionNotFound()
    {
        var coordinator = new ScreenRecordingSessionCoordinator(new FakeScreenRecordingEngine());

        ScreenRecordingControlResult result = await coordinator.PauseAsync(
            ScreenRecordingSessionId.New(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
    }

    [TestMethod]
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

        Assert.IsTrue(stop.Succeeded);
        Assert.AreEqual(ScreenRecordingState.Stopped, stop.Session?.State);
        Assert.AreEqual(ScreenRecordingState.Stopping, engine.StopSession?.State);
        Assert.IsFalse(status.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, status.Failure?.Reason);
    }

    [TestMethod]
    public async Task StopAsync_WhenSessionIsUnknown_ReturnsSessionNotFoundWithoutStoppingEngine()
    {
        var engine = new FakeScreenRecordingEngine();
        var coordinator = new ScreenRecordingSessionCoordinator(engine);

        ScreenRecordingStopResult result = await coordinator.StopAsync(
            ScreenRecordingSessionId.New(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ScreenRecordingFailureReason.SessionNotFound, result.Failure?.Reason);
        Assert.AreEqual(0, engine.StopCallCount);
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
