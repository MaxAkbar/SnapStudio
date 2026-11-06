SnapStudio for Command Palette 1.0 preview (Windows x64)

This optional companion requires SnapStudio 1.3.0 or newer and PowerToys Command Palette.
SnapStudio itself does not require PowerToys or this companion.

This is an unsigned development package. Windows Developer Mode must already be
enabled to register it. The installer does not change Windows security settings,
install PowerToys, change its settings, or add a startup task.

1. Extract the ENTIRE ZIP into a permanent folder. Keep all files together.
2. With Windows Developer Mode enabled, run Install.ps1 in PowerShell.
3. Open SnapStudio 1.3.0 or newer and finish its first-run setup.
4. Enable Command Palette in PowerToys and reload its extensions.
5. Open Command Palette, type SnapStudio, and choose Capture a region,
   Edit clipboard image, or Open most recent image.

Keep the extracted folder in place while the companion is registered. If you move
the folder, run Uninstall.ps1 before moving it, then run Install.ps1 from its new
location. Uninstall.ps1 unregisters only the companion; it does not remove
SnapStudio or saved captures. To remove the files, unregister the companion first.

If you move the SnapStudio portable app, open it from the new location once.
You can also enter the full path to SnapStudio.App.exe in this extension's settings.
Close an older running SnapStudio before using the updated app.

The separate unsigned MSIX is for signing and packaging evaluation. Double-clicking
an unsigned MSIX is not the normal installation path. Signed and Store distribution
are separate release work. PowerToys keyboard invocation, cold startup, and other
display layouts have a narrower validation boundary than the main editor; see the
release notes and bundled integration guide for the exact tested scope.

Release and setup guide:
https://github.com/MaxAkbar/SnapStudio/releases/tag/v1.3.0
https://github.com/MaxAkbar/SnapStudio/blob/v1.3.0/docs/powertoys-integration.md
