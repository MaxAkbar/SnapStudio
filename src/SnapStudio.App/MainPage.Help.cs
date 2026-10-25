using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SnapStudio.App.Help;

namespace SnapStudio.App;

public sealed partial class MainPage
{
    private HelpWindow? _helpWindow;

    private void HelpButton_Click(object sender, RoutedEventArgs args) => ShowHelp();

    private void HelpKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowHelp();
    }

    private void ShowHelp()
    {
        if (_helpWindow is null)
        {
            _helpWindow = new HelpWindow(RequestedTheme);
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }

        _helpWindow.Activate();
    }

    internal void CloseHelp() => _helpWindow?.Close();
}
