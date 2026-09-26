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

    private sealed class FakeCaptureTargetSelector(CaptureTargetSelection? selection) : ICaptureTargetSelector
    {
        public CaptureTargetRequest? LastRequest { get; private set; }

        public Task<CaptureTargetSelection?> SelectTargetAsync(
            CaptureTargetRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(selection);
        }
    }

    private sealed class FakeStillCaptureService(CaptureOutcome outcome) : IStillCaptureService
    {
        public int CallCount { get; private set; }

        public CaptureRequest? LastRequest { get; private set; }

        public Task<CaptureOutcome> CaptureAsync(
            CaptureRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(outcome);
        }
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public CaptureResult? CreatedFromCapture { get; private set; }

        public DocumentId CreatedDocumentId { get; } = DocumentId.New();

        public Task<CaptureDocument> CreateFromCaptureAsync(
            CaptureResult capture,
            CancellationToken cancellationToken)
        {
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
        private readonly List<EditorMessage> _messages = [];

        public IReadOnlyList<EditorMessage> Messages => _messages;

        public Task<EditorMessageSendResult> SendAsync(
            EditorMessage message,
            CancellationToken cancellationToken)
        {
            _messages.Add(message);
            return Task.FromResult(sendResult);
        }
    }
}
