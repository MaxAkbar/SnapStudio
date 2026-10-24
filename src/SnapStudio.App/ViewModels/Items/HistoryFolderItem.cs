namespace SnapStudio.App.ViewModels.Items;

public sealed record HistoryFolderItem(Guid? Id, string Name, int Count, bool IsUnfiled = false, string? SourcePath = null)
{
    public string DisplayName => $"{Name} ({Count})";
    public string FolderGlyph => SourcePath is not null ? "\uE71B" : Id.HasValue ? "\uE8B7" : "\uEB9F";
    public string Description => SourcePath ?? (IsUnfiled ? "Captures outside a group" : Id.HasValue ? "Group for existing captures" : "Every capture in SnapStudio");
}
