using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;

namespace SnapStudio.App.ViewModels;

public sealed partial class ShellViewModel
{
    private bool CanReprojectPerspectiveMoves => _currentDocument is not null && ObjectSnappingEnabled && IsPerspectiveGridSnapEnabled;

    private bool TryCreatePerspectiveMove(AnnotationObject original, RectD proposed, out AnnotationObject? projected)
    {
        projected = null;
        var document = _currentDocument;
        return CanReprojectPerspectiveMoves && document is not null && PerspectiveObjectBinding.TryReproject(original, proposed,
            new(0, 0, document.SourceImage.Width, document.SourceImage.Height), CanvasGridPerspective,
            CanvasZoom, CanvasZoom, out projected);
    }

    // The same proposed bounds drive preview and commit. Re-projected bounds must not become another input delta.
    private async Task<bool> TryCommitPerspectiveMovesAsync(IReadOnlyDictionary<Guid, RectD> proposals, string name, CancellationToken token)
    {
        var document = _currentDocument;
        if (!CanReprojectPerspectiveMoves || document is null || _editCommandStack is null || _documentRepository is null || proposals.Count == 0) return false;
        var originals = document.Annotations.Where(a => proposals.ContainsKey(a.Id)).ToArray();
        if (originals.Length != proposals.Count) return false;
        var commands = new List<IEditCommand>();
        bool boundObject = false;
        foreach (var original in originals)
        {
            RectD bounds = proposals[original.Id];
            AnnotationVector? vector = original.Vector;
            if (TryCreatePerspectiveMove(original, bounds, out var projected))
            {
                boundObject = true;
                bounds = projected?.Bounds ?? original.Bounds;
                vector = projected?.Vector ?? original.Vector;
            }
            if (bounds != original.Bounds) commands.Add(new UpdateAnnotationBoundsCommand(original.Id, original.Bounds, bounds, name));
            if (original.Vector is not null && vector is not null && vector != original.Vector)
                commands.Add(new UpdateAnnotationVectorCommand(original.Id, original.Vector, vector));
        }
        if (!boundObject) return false;
        if (commands.Count > 0)
        {
            await _editCommandStack.ExecuteAsync(document, new CompositeEditCommand(name, commands), token).ConfigureAwait(true);
            RefreshSelection(syncStyle: false);
            await SaveCurrentDocumentAsync($"{name} complete.", token).ConfigureAwait(true);
        }
        return true;
    }

    private async Task<bool> TryDuplicatePerspectiveObjectsAsync(CancellationToken token)
    {
        var document = _currentDocument;
        if (!CanReprojectPerspectiveMoves || document is null || _editCommandStack is null || _documentRepository is null || !HasSelectedAnnotation) return false;
        var originals = document.Annotations.Where(a => SelectedAnnotationIds.Contains(a.Id)).ToArray();
        if (!originals.Any(PerspectiveObjectBinding.IsPerspectiveObject)) return false;
        var proposals = AnnotationBoundsEditor.MoveSelection(originals.ToDictionary(a => a.Id, a => a.Bounds), 10, 10,
            new(document.SourceImage.Width, document.SourceImage.Height));
        var copies = new List<AnnotationObject>();
        bool boundObject = false;
        foreach (var original in originals)
        {
            var copy = CloneAnnotation(original, proposals[original.Id]);
            if (TryCreatePerspectiveMove(original, proposals[original.Id], out var projected))
            {
                boundObject = true;
                copy = CloneAnnotation(projected ?? original, projected?.Bounds ?? original.Bounds);
            }
            copy.Id = Guid.NewGuid();
            copies.Add(copy);
        }
        if (!boundObject) return false;
        await _editCommandStack.ExecuteAsync(document, new CompositeEditCommand("Duplicate Objects",
            copies.Select(a => (IEditCommand)new AddAnnotationCommand(a)).ToArray()), token).ConfigureAwait(true);
        _annotationSelection.SelectMany(document, copies.Select(a => a.Id));
        RefreshSelection();
        await SaveCurrentDocumentAsync("Objects duplicated.", token).ConfigureAwait(true);
        return true;
    }
}
