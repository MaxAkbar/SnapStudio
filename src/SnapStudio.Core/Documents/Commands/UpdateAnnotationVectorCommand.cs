namespace SnapStudio.Core.Documents;

public sealed class UpdateAnnotationVectorCommand(Guid id, AnnotationVector before, AnnotationVector after) : IEditCommand
{
    public string DisplayName => "Edit SVG object";
    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken token) => SetAsync(document, after, token);
    public ValueTask RevertAsync(CaptureDocument document, CancellationToken token) => SetAsync(document, before, token);
    private ValueTask SetAsync(CaptureDocument document, AnnotationVector vector, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        AnnotationObject item = document.Annotations.FirstOrDefault(item => item.Id == id && item.Kind == AnnotationKind.Vector)
            ?? throw new InvalidOperationException("The SVG object is no longer present.");
        item.Vector = vector;
        return ValueTask.CompletedTask;
    }
}
