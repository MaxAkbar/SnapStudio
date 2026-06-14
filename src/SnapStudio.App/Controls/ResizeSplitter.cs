using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace SnapStudio.App.Controls;

public sealed class ResizeSplitter : Control
{
    public ResizeSplitter()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
