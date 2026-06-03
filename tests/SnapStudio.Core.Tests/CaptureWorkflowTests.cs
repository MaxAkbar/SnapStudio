using SnapStudio.Core.Capture;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Messaging;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Tests;

public sealed class CaptureWorkflowTests
{
    [Fact]
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

        Assert.True(result.Succeeded);
        Assert.Equal(repository.CreatedDocumentId, result.DocumentId);
        Assert.Equal(capture, repository.CreatedFromCapture);
        Assert.Equal(CaptureTargetKind.Region, captureService.LastRequest?.TargetKind);
        Assert.False(captureService.LastRequest?.IncludeCursor);
        Assert.Equal(TimeSpan.FromMilliseconds(250), captureService.LastRequest?.Delay);
        Assert.Equal("region-1", captureService.LastRequest?.TargetHint);
        Assert.Equal(new RectD(1, 2, 30, 40), captureService.LastRequest?.Bounds);
        Assert.Equal(CaptureTargetSelectionMode.Picker, selector.LastRequest?.SelectionMode);
        Assert.Equal("Picker", captureService.LastRequest?.TargetMetadata?["selectionMode"]);

        var completed = Assert.IsType<CaptureCompletedEditorMessage>(
            Assert.Single(editorMessages.Messages));
        Assert.Equal(captureId, completed.CaptureId);
        Assert.Equal(repository.CreatedDocumentId, completed.DocumentId);
        Assert.Equal("capture.png", completed.SourceImagePath);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(CaptureFailureReason.Cancelled, result.Failure?.Reason);
        Assert.Equal(0, captureService.CallCount);
        Assert.Null(repository.CreatedFromCapture);
        Assert.Empty(editorMessages.Messages);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(CaptureFailureReason.Unsupported, result.Failure?.Reason);
        Assert.Equal(0, captureService.CallCount);
        Assert.Null(repository.CreatedFromCapture);

        var failed = Assert.IsType<CaptureFailedEditorMessage>(
            Assert.Single(editorMessages.Messages));
        Assert.Equal(CaptureFailureReason.Unsupported, failed.Reason);
    }

    [Fact]
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

        Assert.False(result.Succeeded);
        Assert.Equal(failure, result.Failure);
        Assert.Null(repository.CreatedFromCapture);

        var failed = Assert.IsType<CaptureFailedEditorMessage>(
            Assert.Single(editorMessages.Messages));
        Assert.Equal(CaptureFailureReason.PermissionDenied, failed.Reason);
        Assert.Equal("Capture permission was denied.", failed.Message);
    }

    [Fact]
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

        Assert.True(result.Succeeded);
        Assert.NotNull(result.DocumentId);
        Assert.False(result.EditorNotification?.Succeeded);
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
