using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Messaging;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class CaptureWorkflowTests
{
    [TestMethod]
    public async Task CaptureAsync_WhenCaptureSucceeds_PersistsDocumentAndNotifiesEditor()
    {
        var captureId = CaptureId.New();
        var capture = new CaptureResult(
            captureId,
            DateTimeOffset.UtcNow,
            new ImageAsset("capture.png", 100, 80, ImagePixelFormat.Bgra32),
            new Dictionary<string, string>());
        var selector = new FakeCaptureTargetSelector(
            new CaptureTargetSelection(
                CaptureTargetKind.Region,
                "region-1",
                new RectD(1, 2, 30, 40),
                new Dictionary<string, string>
                {
                    ["selectionMode"] = "Picker"
                }));
        var captureService = new FakeStillCaptureService(CaptureOutcome.Success(capture));
        var repository = new FakeDocumentRepository();
        var editorMessages = new FakeEditorMessageClient(EditorMessageSendResult.Success());
        var workflow = new CaptureWorkflow(selector, captureService, repository, editorMessages);

        CaptureWorkflowResult result = await workflow.CaptureAsync(
            new CaptureWorkflowRequest(
                [CaptureTargetKind.Region],
                IncludeCursor: false,
                Delay: TimeSpan.FromMilliseconds(250),
                CaptureTargetSelectionMode.Picker),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(repository.CreatedDocumentId, result.DocumentId);
        Assert.AreEqual(capture, repository.CreatedFromCapture);
        Assert.AreEqual(CaptureTargetKind.Region, captureService.LastRequest?.TargetKind);
        Assert.IsFalse(captureService.LastRequest?.IncludeCursor);
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), captureService.LastRequest?.Delay);
        Assert.AreEqual("region-1", captureService.LastRequest?.TargetHint);
        Assert.AreEqual(new RectD(1, 2, 30, 40), captureService.LastRequest?.Bounds);
        Assert.AreEqual(CaptureTargetSelectionMode.Picker, selector.LastRequest?.SelectionMode);
        Assert.IsFalse(selector.LastRequest?.IncludeCursor);
        Assert.AreEqual("Picker", captureService.LastRequest?.TargetMetadata?["selectionMode"]);

        var completed = Assert.IsExactInstanceOfType<CaptureCompletedEditorMessage>(
            Assert.ContainsSingle(editorMessages.Messages));
        Assert.AreEqual(captureId, completed.CaptureId);
        Assert.AreEqual(repository.CreatedDocumentId, completed.DocumentId);
        Assert.AreEqual("capture.png", completed.SourceImagePath);
    }

    [TestMethod]
    public async Task CaptureAsync_WhenSelectionIsCancelled_DoesNotCaptureOrNotify()
    {
        var selector = new FakeCaptureTargetSelector(selection: null);
        var captureService = new FakeStillCaptureService(
            CaptureOutcome.Failed(new CaptureFailure(CaptureFailureReason.Unknown, "Should not run.")));
        var repository = new FakeDocumentRepository();
        var editorMessages = new FakeEditorMessageClient(EditorMessageSendResult.Success());
        var workflow = new CaptureWorkflow(selector, captureService, repository, editorMessages);

        CaptureWorkflowResult result = await workflow.CaptureAsync(
            CaptureWorkflowRequest.FullScreen(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(CaptureFailureReason.Cancelled, result.Failure?.Reason);
        Assert.IsTrue(selector.LastRequest?.IncludeCursor);
        Assert.AreEqual(0, captureService.CallCount);
        Assert.IsNull(repository.CreatedFromCapture);
        Assert.IsEmpty(editorMessages.Messages);
    }

    [TestMethod]
    public async Task CaptureAsync_WhenTargetSelectionIsUnsupported_NotifiesFailure()
    {
        var selector = new FakeCaptureTargetSelector(
            new CaptureTargetSelection(CaptureTargetKind.Window, "window-1", Bounds: null));
        var captureService = new FakeStillCaptureService(
            CaptureOutcome.Failed(new CaptureFailure(CaptureFailureReason.Unknown, "Should not run.")));
        var repository = new FakeDocumentRepository();
        var editorMessages = new FakeEditorMessageClient(EditorMessageSendResult.Success());
        var workflow = new CaptureWorkflow(selector, captureService, repository, editorMessages);

        CaptureWorkflowResult result = await workflow.CaptureAsync(
            new CaptureWorkflowRequest(
                [CaptureTargetKind.FullScreen],
                IncludeCursor: true,
                Delay: TimeSpan.Zero),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(CaptureFailureReason.Unsupported, result.Failure?.Reason);
        Assert.AreEqual(0, captureService.CallCount);
        Assert.IsNull(repository.CreatedFromCapture);

        var failed = Assert.IsExactInstanceOfType<CaptureFailedEditorMessage>(
            Assert.ContainsSingle(editorMessages.Messages));
        Assert.AreEqual(CaptureFailureReason.Unsupported, failed.Reason);
    }

    [TestMethod]
    public async Task CaptureAsync_WhenCaptureFails_NotifiesEditorAndDoesNotPersist()
    {
        var failure = new CaptureFailure(CaptureFailureReason.PermissionDenied, "Capture permission was denied.");
        var selector = new FakeCaptureTargetSelector(
            new CaptureTargetSelection(CaptureTargetKind.FullScreen, TargetId: null, Bounds: null));
        var captureService = new FakeStillCaptureService(CaptureOutcome.Failed(failure));
        var repository = new FakeDocumentRepository();
        var editorMessages = new FakeEditorMessageClient(EditorMessageSendResult.Success());
        var workflow = new CaptureWorkflow(selector, captureService, repository, editorMessages);

        CaptureWorkflowResult result = await workflow.CaptureAsync(
            CaptureWorkflowRequest.FullScreen(),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(failure, result.Failure);
        Assert.IsNull(repository.CreatedFromCapture);

        var failed = Assert.IsExactInstanceOfType<CaptureFailedEditorMessage>(
            Assert.ContainsSingle(editorMessages.Messages));
        Assert.AreEqual(CaptureFailureReason.PermissionDenied, failed.Reason);
        Assert.AreEqual("Capture permission was denied.", failed.Message);
    }

    [TestMethod]
    public async Task CaptureAsync_WhenEditorNotificationFails_StillReturnsSuccessfulCapture()
    {
        var capture = new CaptureResult(
            CaptureId.New(),
            DateTimeOffset.UtcNow,
            new ImageAsset("capture.png", 100, 80, ImagePixelFormat.Bgra32),
            new Dictionary<string, string>());
        var workflow = new CaptureWorkflow(
            new FakeCaptureTargetSelector(
                new CaptureTargetSelection(CaptureTargetKind.FullScreen, TargetId: null, Bounds: null)),
            new FakeStillCaptureService(CaptureOutcome.Success(capture)),
            new FakeDocumentRepository(),
            new FakeEditorMessageClient(EditorMessageSendResult.Failed("Editor is not running.")));

        CaptureWorkflowResult result = await workflow.CaptureAsync(
            CaptureWorkflowRequest.FullScreen(),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.DocumentId);
        Assert.IsFalse(result.EditorNotification?.Succeeded);
    }

    [TestMethod]
    [DataRow(CaptureTargetKind.Region, CaptureTargetSelectionMode.Automatic)]
    [DataRow(CaptureTargetKind.Window, CaptureTargetSelectionMode.Automatic)]
    [DataRow(CaptureTargetKind.Display, CaptureTargetSelectionMode.Automatic)]
    [DataRow(CaptureTargetKind.FullScreen, CaptureTargetSelectionMode.Automatic)]
    [DataRow(CaptureTargetKind.Window, CaptureTargetSelectionMode.Picker)]
    [DataRow(CaptureTargetKind.Display, CaptureTargetSelectionMode.Picker)]
    public async Task CaptureAsync_MinimizesBeforeDesktopAccessAndRestoresAfterSaving(
        CaptureTargetKind kind, CaptureTargetSelectionMode mode)
    {
        var calls = new List<string>();
        var window = new FakeCaptureWindowController(calls);
        var selector = new FakeCaptureTargetSelector(new(kind, "target", null))
        {
            OnCall = () => calls.Add("select")
        };
        var capture = new FakeStillCaptureService(CaptureOutcome.Success(CreateCapture()))
        {
            OnCall = () => { Assert.IsTrue(window.IsMinimized); calls.Add("capture"); }
        };
        var repository = new FakeDocumentRepository { OnCall = () => calls.Add("save") };
        var editor = new FakeEditorMessageClient(EditorMessageSendResult.Success()) { OnCall = () => calls.Add("notify") };
        var workflow = new CaptureWorkflow(selector, capture, repository, editor, window);

        var result = await workflow.CaptureAsync(new([kind], false, TimeSpan.FromSeconds(5), mode), CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(TimeSpan.FromSeconds(5), capture.LastRequest?.Delay);
        Assert.IsFalse(window.IsMinimized);
        Assert.AreSequenceEqual(kind == CaptureTargetKind.Region
            ? new[] { "minimize", "select", "capture", "save", "notify", "restore" }
            : new[] { "select", "minimize", "capture", "save", "notify", "restore" }, calls);
    }

    [TestMethod]
    [DataRow(CaptureTargetKind.Region)]
    [DataRow(CaptureTargetKind.Window)]
    public async Task CaptureAsync_CancelledSelectionRestoresOnlyWhenMinimized(CaptureTargetKind kind)
    {
        var calls = new List<string>();
        var window = new FakeCaptureWindowController(calls);
        var selector = new FakeCaptureTargetSelector(null) { OnCall = () => calls.Add("select") };
        var workflow = new CaptureWorkflow(selector, new FakeStillCaptureService(CaptureOutcome.Success(CreateCapture())),
            new FakeDocumentRepository(), new FakeEditorMessageClient(EditorMessageSendResult.Success()), window);

        var result = await workflow.CaptureAsync(new([kind], false, TimeSpan.Zero), CancellationToken.None);

        Assert.AreEqual(CaptureFailureReason.Cancelled, result.Failure?.Reason);
        Assert.AreSequenceEqual(kind == CaptureTargetKind.Region
            ? new[] { "minimize", "select", "restore" } : new[] { "select" }, calls);
        Assert.IsFalse(window.IsMinimized);
    }

    [TestMethod]
    [DataRow("select")]
    [DataRow("capture")]
    [DataRow("save")]
    [DataRow("notify")]
    public async Task CaptureAsync_WhenAnyStageThrows_RestoresWindow(string failingStage)
    {
        var calls = new List<string>();
        var window = new FakeCaptureWindowController(calls);
        void Stage(string name) { if (name == failingStage) throw new InvalidOperationException("Test failure"); }
        var workflow = new CaptureWorkflow(
            new FakeCaptureTargetSelector(new(CaptureTargetKind.Region, "region", null)) { OnCall = () => Stage("select") },
            new FakeStillCaptureService(CaptureOutcome.Success(CreateCapture())) { OnCall = () => Stage("capture") },
            new FakeDocumentRepository { OnCall = () => Stage("save") },
            new FakeEditorMessageClient(EditorMessageSendResult.Success()) { OnCall = () => Stage("notify") }, window);

        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.CaptureAsync(
            new([CaptureTargetKind.Region], false, TimeSpan.Zero), CancellationToken.None));

        Assert.AreSequenceEqual(new[] { "minimize", "restore" }, calls);
        Assert.IsFalse(window.IsMinimized);
    }

    [TestMethod]
    public async Task CaptureAsync_WhenTokenIsCancelledDuringCapture_StillRestoresWindow()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = new List<string>();
        var window = new FakeCaptureWindowController(calls);
        var capture = new FakeStillCaptureService(CaptureOutcome.Success(CreateCapture()))
        {
            OnCall = () => { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
        };
        var workflow = new CaptureWorkflow(new FakeCaptureTargetSelector(new(CaptureTargetKind.FullScreen, "screen", null)),
            capture, new FakeDocumentRepository(), new FakeEditorMessageClient(EditorMessageSendResult.Success()), window);

        await Assert.ThrowsAsync<OperationCanceledException>(() => workflow.CaptureAsync(
            CaptureWorkflowRequest.FullScreen(), cancellation.Token));

        Assert.AreSequenceEqual(new[] { "minimize", "restore" }, calls);
        Assert.IsFalse(window.IsMinimized);
    }

    [TestMethod]
    public async Task CaptureAsync_WhenFrameAcquisitionIsPending_KeepsWindowMinimized()
    {
        var calls = new List<string>();
        var window = new FakeCaptureWindowController(calls);
        var frame = new TaskCompletionSource<CaptureOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new CaptureWorkflow(new FakeCaptureTargetSelector(new(CaptureTargetKind.FullScreen, "screen", null)),
            new FakeStillCaptureService(CaptureOutcome.Success(CreateCapture())) { PendingOutcome = frame.Task },
            new FakeDocumentRepository(), new FakeEditorMessageClient(EditorMessageSendResult.Success()), window);

        Task<CaptureWorkflowResult> capture = workflow.CaptureAsync(CaptureWorkflowRequest.FullScreen(), CancellationToken.None);
        Assert.IsTrue(window.IsMinimized);
        Assert.IsFalse(capture.IsCompleted);
        frame.SetResult(CaptureOutcome.Failed(new(CaptureFailureReason.TargetUnavailable, "No frame")));
        var result = await capture;

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(window.IsMinimized);
        Assert.AreSequenceEqual(new[] { "minimize", "restore" }, calls);
    }

    private static CaptureResult CreateCapture() => new(CaptureId.New(), DateTimeOffset.UtcNow,
        new ImageAsset("capture.png", 100, 80, ImagePixelFormat.Bgra32), new Dictionary<string, string>());

    private sealed class FakeCaptureWindowController(List<string> calls) : ICaptureWindowController, IAsyncDisposable
    {
        public bool IsMinimized { get; private set; }
        public Task<IAsyncDisposable> MinimizeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.IsFalse(IsMinimized);
            IsMinimized = true; calls.Add("minimize");
            return Task.FromResult<IAsyncDisposable>(this);
        }
        public ValueTask DisposeAsync()
        {
            Assert.IsTrue(IsMinimized);
            IsMinimized = false; calls.Add("restore");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeCaptureTargetSelector(CaptureTargetSelection? selection) : ICaptureTargetSelector
    {
        public Action? OnCall { get; init; }

        public CaptureTargetRequest? LastRequest { get; private set; }

        public Task<CaptureTargetSelection?> SelectTargetAsync(
            CaptureTargetRequest request,
            CancellationToken cancellationToken)
        {
            OnCall?.Invoke();
            LastRequest = request;
            return Task.FromResult(selection);
        }
    }

    private sealed class FakeStillCaptureService(CaptureOutcome outcome) : IStillCaptureService
    {
        public Action? OnCall { get; init; }
        public Task<CaptureOutcome>? PendingOutcome { get; init; }

        public int CallCount { get; private set; }

        public CaptureRequest? LastRequest { get; private set; }

        public Task<CaptureOutcome> CaptureAsync(
            CaptureRequest request,
            CancellationToken cancellationToken)
        {
            OnCall?.Invoke();
            CallCount++;
            LastRequest = request;
            return PendingOutcome ?? Task.FromResult(outcome);
        }
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public Action? OnCall { get; init; }

        public CaptureResult? CreatedFromCapture { get; private set; }

        public DocumentId CreatedDocumentId { get; } = DocumentId.New();

        public Task<CaptureDocument> CreateFromCaptureAsync(
            CaptureResult capture,
            CancellationToken cancellationToken)
        {
            OnCall?.Invoke();
            CreatedFromCapture = capture;

            return Task.FromResult(new CaptureDocument
            {
                Id = CreatedDocumentId,
                SourceImage = capture.SourceImage
            });
        }

        public Task<CaptureDocument?> GetAsync(DocumentId id, CancellationToken cancellationToken)
        {
            return Task.FromResult<CaptureDocument?>(null);
        }

        public Task SaveAsync(CaptureDocument document, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(DocumentId id, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class FakeEditorMessageClient(EditorMessageSendResult sendResult) : IEditorMessageClient
    {
        public Action? OnCall { get; init; }

        private readonly List<EditorMessage> _messages = [];

        public IReadOnlyList<EditorMessage> Messages => _messages;

        public Task<EditorMessageSendResult> SendAsync(
            EditorMessage message,
            CancellationToken cancellationToken)
        {
            OnCall?.Invoke();
            _messages.Add(message);
            return Task.FromResult(sendResult);
        }
    }
}
