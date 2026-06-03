using SnapStudio.Core.Documents;
using SnapStudio.Core.Messaging;

namespace SnapStudio.Core.Capture;

public sealed class CaptureWorkflow : ICaptureWorkflow
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IEditorMessageClient _editorMessages;
    private readonly IStillCaptureService _stillCaptureService;
    private readonly ICaptureTargetSelector _targetSelector;

    public CaptureWorkflow(
        ICaptureTargetSelector targetSelector,
        IStillCaptureService stillCaptureService,
        IDocumentRepository documentRepository,
        IEditorMessageClient editorMessages)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(stillCaptureService);
        ArgumentNullException.ThrowIfNull(documentRepository);
        ArgumentNullException.ThrowIfNull(editorMessages);

        _targetSelector = targetSelector;
        _stillCaptureService = stillCaptureService;
        _documentRepository = documentRepository;
        _editorMessages = editorMessages;
    }

    public async Task<CaptureWorkflowResult> CaptureAsync(
        CaptureWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AllowedTargets.Count == 0)
        {
            throw new ArgumentException("At least one capture target kind must be allowed.", nameof(request));
        }

        CaptureTargetSelection? selection = await _targetSelector
            .SelectTargetAsync(
                new CaptureTargetRequest(
                    request.AllowedTargets,
                    AllowDelayedCapture: request.Delay > TimeSpan.Zero,
                    request.SelectionMode),
                cancellationToken)
            .ConfigureAwait(false);

        if (selection is null)
        {
            return CaptureWorkflowResult.Failed(
                new CaptureFailure(CaptureFailureReason.Cancelled, "Capture was cancelled."));
        }

        if (!request.AllowedTargets.Contains(selection.TargetKind))
        {
            var failure = new CaptureFailure(
                CaptureFailureReason.Unsupported,
                $"Capture target '{selection.TargetKind}' is not allowed for this workflow.");

            return CaptureWorkflowResult.Failed(
                failure,
                ToWorkflowNotification(
                    await NotifyFailureAsync(failure, cancellationToken).ConfigureAwait(false)));
        }

        CaptureOutcome outcome = await _stillCaptureService
            .CaptureAsync(
                new CaptureRequest(
                    selection.TargetKind,
                    request.IncludeCursor,
                    request.Delay,
                    selection.TargetId,
                    selection.Bounds,
                    selection.Metadata),
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Failure is not null || outcome.Capture is null)
        {
            CaptureFailure failure = outcome.Failure
                ?? new CaptureFailure(CaptureFailureReason.Unknown, "Capture did not produce an image.");

            return CaptureWorkflowResult.Failed(
                failure,
                ToWorkflowNotification(
                    await NotifyFailureAsync(failure, cancellationToken).ConfigureAwait(false)));
        }

        CaptureDocument document = await _documentRepository
            .CreateFromCaptureAsync(outcome.Capture, cancellationToken)
            .ConfigureAwait(false);

        EditorMessageSendResult notification = await _editorMessages
            .SendAsync(
                new CaptureCompletedEditorMessage(
                    outcome.Capture.Id,
                    document.Id,
                    outcome.Capture.SourceImage.Path),
                cancellationToken)
            .ConfigureAwait(false);

        return CaptureWorkflowResult.Success(document.Id, ToWorkflowNotification(notification));
    }

    private Task<EditorMessageSendResult> NotifyFailureAsync(
        CaptureFailure failure,
        CancellationToken cancellationToken)
    {
        return _editorMessages.SendAsync(
            new CaptureFailedEditorMessage(failure.Reason, failure.Message),
            cancellationToken);
    }

    private static CaptureWorkflowNotification ToWorkflowNotification(
        EditorMessageSendResult sendResult)
    {
        return new CaptureWorkflowNotification(sendResult.Succeeded, sendResult.ErrorMessage);
    }
}
