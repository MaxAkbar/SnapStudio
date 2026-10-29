namespace SnapStudio.Core.Documents;

public sealed class AddObjectGroupCommand(AnnotationObjectGroup group) : IEditCommand
{
    public string DisplayName => "Add Object Group";
    public ValueTask ApplyAsync(CaptureDocument document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (document.ObjectGroups.All(item => item.Id != group.Id)) document.ObjectGroups.Add(group);
        return ValueTask.CompletedTask;
    }
    public ValueTask RevertAsync(CaptureDocument document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); document.ObjectGroups.RemoveAll(item => item.Id == group.Id);
        return ValueTask.CompletedTask;
    }
}
