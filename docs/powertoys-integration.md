# Optional PowerToys integration

Use SnapStudio 1.3.0 or newer. The optional Command Palette companion is a separately distributed, unsigned preview; SnapStudio 1.2.0 does not support its commands.

SnapStudio works independently of PowerToys. The normal editor, capture shortcuts, clipboard, library, and export commands do not require PowerToys. SnapStudio does not detect, install, launch, or show reminders about PowerToys. Its regular build and portable package do not contain the Command Palette SDK or companion.

## Clipboard colors

Open the annotation color picker and select **Use clipboard color** to apply a copied RGB hex color. Supported formats are `#RRGGBB`, `RRGGBB`, `#RGB`, and `RGB`, with surrounding whitespace allowed. The color is opaque; existing annotation opacity remains a separate setting. Changes use the normal annotation editing and undo workflow.

This works with any application that copies a hex color. With PowerToys Color Picker, use its default HEX output, pick a screen color, and apply it in SnapStudio. Invalid clipboard text leaves the current color unchanged. SnapStudio only reads clipboard text when you click the action; it does not monitor the clipboard.

## Command Palette companion

The separate **SnapStudio for Command Palette** package adds three commands:

- **SnapStudio: Capture a region** — uses the normal region selector and capture preferences.
- **SnapStudio: Edit clipboard image** — opens the clipboard image as a new editable document, preserving the document already in the library.
- **SnapStudio: Open most recent image** — opens the most recently updated image across all library folders and tags, independently of the current history filter.

Install the companion only if you use [PowerToys Command Palette](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview). Download the companion's **development ZIP** from the SnapStudio 1.3.0 release, extract the entire archive into a permanent folder, and run its `Install.ps1` in PowerShell. This unsigned preview requires Windows Developer Mode to be enabled by the user; the installer does not change Windows security settings or PowerToys settings. Keep the extracted folder in place while the companion is registered. The separate unsigned MSIX is for signing and packaging evaluation, not double-click installation.

Enable Command Palette in PowerToys and open SnapStudio 1.3.0 or newer at least once. The companion then discovers the last compatible SnapStudio you opened and starts it when needed. If you move the portable app, open it from the new folder once. The extension's settings also accept the full path to `SnapStudio.App.exe` as an explicit override. A running editor is reused; close an older editor before trying the updated companion. Run the development ZIP's `Uninstall.ps1` before moving or removing its folder.

Finish first-run setup, open dialogs, or active edits in SnapStudio before invoking a command. Capture and recording already in progress cause the command to be rejected. The companion reports failures in Command Palette; missing PowerToys never creates a warning in SnapStudio. Uninstalling the companion does not affect saved captures or the editor.

The companion uses the official [out-of-process .NET extension model](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/extensibility-overview). It requires a Command Palette release compatible with `Microsoft.CommandPalette.Extensions` 0.12.260812002. It is intentionally excluded from `SnapStudio.sln` and the main app's project references so ordinary development and release builds do not restore PowerToys packages.

## Build and package the companion

Use Windows 11, the .NET 10 SDK, and a Windows SDK with MakeAppx:

```powershell
dotnet build integrations/SnapStudio.CommandPalette/SnapStudio.CommandPalette.csproj -c Release -p:Platform=x64
./eng/package-command-palette.ps1 -Platform x64
```

The packaging script publishes a self-contained companion and creates an unsigned MSIX and a development ZIP under `artifacts/command-palette/x64/Release`. The ZIP includes the loose manifest, installation and removal scripts, and setup instructions. The script does not build, bundle, install, or change SnapStudio or PowerToys. Choose a fresh `-OutputDirectory` when rebuilding a package. ARM64 can be built with `-Platform ARM64`; native ARM64 validation is separate.

For local development with Windows Developer Mode already enabled, register the generated loose package:

```powershell
Add-AppxPackage -Register ./artifacts/command-palette/x64/Release/package/AppxManifest.xml
```

Reload Command Palette extensions after registration. For distribution, sign the MSIX with an appropriate trusted certificate, using `-PackagePublisher` to match the signing identity, or publish through the Microsoft Store. An unsigned MSIX is a build artifact, not a generally installable release. Do not change the normal SnapStudio installer to install this package automatically.

To remove a development registration:

```powershell
Get-AppxPackage -Name SnapStudio.CommandPalette | Remove-AppxPackage
```

## Local connection

The editor exposes a current-user-only, session-specific named pipe, separate from ExactFrame's version 1 region-capture protocol and the existing editor notification channel. The default name is `SnapStudio.WorkspaceCommands.v1.Session{WindowsSessionId}`. Messages are UTF-8 newline-delimited JSON, limited to 8,192 bytes. Only the three named commands are accepted; no arbitrary executable, path, script, or database query is accepted over the connection.

Request and response example:

```json
{"protocolVersion":1,"requestId":"2a4de139-078e-46bd-9e10-b1886c003354","command":"open-latest"}
{"protocolVersion":1,"requestId":"2a4de139-078e-46bd-9e10-b1886c003354","succeeded":true,"error":null,"documentId":"d81c2503-a71a-4b3d-aa26-179706506330"}
```

The client validates the response version and request ID. It allows 30 seconds for cold startup, bounds command execution, and does not retry after sending a command. Disconnecting cancels pending work. The editor keeps owning its documents and storage. The companion receives status and a document ID, not screenshot pixels or library contents. This same-user channel is not an authentication boundary between programs running as that Windows user.

The editor records its executable location in `%LOCALAPPDATA%/SnapStudio/workspace-app.json` on a best-effort basis. A missing or unwritable discovery file never blocks the editor. The companion verifies the executable name and `workspaceCommandProtocol` in the adjacent `SnapStudio.integration.json` before cold launch. Explicit application paths must also pass that check.

For isolated development checks, set `SNAPSTUDIO_DATA_ROOT` to a disposable profile and `SNAPSTUDIO_COMMAND_PIPE` to the same unique pipe name in the editor and test client. Set `SNAPSTUDIO_INTEGRATION_PIPE` separately to keep ExactFrame checks isolated. Do not target a user's running editor or library during automated validation.

## Validation

Windows x64 checks covered a warning-free editor and companion build, MSIX creation and development registration, COM activation and enumeration of all three commands, native region capture, reopening the latest image, clipboard-image creation, clipboard color application, rejection while a dialog was open, and restoring the workspace after the launcher disconnected during region selection. The normal editor output contained no Command Palette assemblies. Development registration was removed after validation.

The focused protocol, discovery, color, cancellation, and ExactFrame checks passed, and the 1.3.0 Release test run passed all 935 tests. The development ZIP's installer and removal path are checked independently from the editor package. No storage implementation changes were made for this integration. Full keyboard invocation through Command Palette, cold launch with no other editor running, signed-package installation, and native ARM64 execution remain preview QA items. The release's attached validation report records the exact package verification boundary.
