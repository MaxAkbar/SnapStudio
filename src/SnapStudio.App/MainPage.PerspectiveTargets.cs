using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Rendering;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private void PerspectiveTargets_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement anchor) return;
        var panel = new StackPanel { Width = 280, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Perspective snap targets", FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = ViewModel.HasSelectedAnnotation ? "Applies to the selected objects." : "Default for new drawings in this session.", TextWrapping = TextWrapping.Wrap });
        var all = new Button { Content = "All guides" }; var none = new Button { Content = "None" };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; buttons.Children.Add(all); buttons.Children.Add(none); panel.Children.Add(buttons);
        var boxes = new Dictionary<GridGuideFamily, CheckBox>(); bool refreshing = false;
        void Refresh()
        {
            refreshing = true; var values = ViewModel.PerspectiveTargetValues;
            foreach (var (family, box) in boxes) { var states = values.Select(value => value.Allows(family)).Distinct().ToArray(); box.IsChecked = states.Length > 1 ? null : states[0]; }
            refreshing = false;
        }
        async Task Change(PerspectiveSnapTargets targets, bool? enabled)
        {
            await RunToolDesignUiActionAsync(() => ViewModel.SetPerspectiveTargetsAsync(targets, enabled, CancellationToken.None)); Refresh();
        }
        foreach (var family in CanvasGridFamilies.Active((CanvasGridMode)ViewModel.CanvasGridMode))
        {
            var box = new CheckBox { Content = PerspectiveGuideEditing.Title(family, (CanvasGridMode)ViewModel.CanvasGridMode) };
            AutomationProperties.SetName(box, $"Snap to {box.Content}"); boxes.Add(family, box); panel.Children.Add(box);
            box.Click += async (_, _) => { if (!refreshing) await Change(PerspectiveTargetFilter.For(family), box.IsChecked != false); };
        }
        all.Click += async (_, _) => await Change(PerspectiveSnapTargets.All, null);
        none.Click += async (_, _) => await Change(PerspectiveSnapTargets.None, null);
        panel.Children.Add(new TextBlock { Text = "Guides stay visible. Object alignment remains enabled. Rectangles keep their plane; opposing curved VPs share a curve direction. Hold Alt for a free move.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        Refresh(); new Flyout { Content = panel }.ShowAt(anchor);
    }
}
