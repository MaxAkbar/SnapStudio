using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace SnapStudio.App.Controls;

internal static class IconForeground
{
    public static void InheritFromOwner(UserControl icon)
    {
        // Preserve explicit drawing colors, including custom layer color labels.
        if (icon.ReadLocalValue(Control.ForegroundProperty) != DependencyProperty.UnsetValue) return;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(icon); parent is not null;
            parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not Control owner) continue;
            icon.SetBinding(Control.ForegroundProperty, new Binding
            {
                Source = owner,
                Path = new PropertyPath(nameof(Control.Foreground))
            });
            return;
        }
    }
}
