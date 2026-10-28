using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace SnapStudio.App.Controls;

public sealed class EditorSurface : Grid
{
    private InputSystemCursor? _cursor;
    private InputSystemCursorShape? _shape;

    public EditorSurface() => Unloaded += (_, _) => { ProtectedCursor = null; _cursor?.Dispose(); _cursor = null; _shape = null; };

    public void UpdateCursor(bool marquee, bool move, InputSystemCursorShape? interaction = null)
    {
        InputSystemCursorShape shape = interaction ?? (marquee ? InputSystemCursorShape.Cross : move ? InputSystemCursorShape.SizeAll : InputSystemCursorShape.Arrow);
        if (_shape == shape) return;
        InputSystemCursor? previous = _cursor;
        ProtectedCursor = _cursor = InputSystemCursor.Create(shape);
        _shape = shape;
        previous?.Dispose();
    }
}
