# Development Setup

## Visual Studio

Open `SnapStudio.sln`, not `SnapStudio.slnx`.

Recommended Visual Studio setup:

- .NET 10 SDK installed.
- Windows App SDK / WinUI tooling installed.
- Windows 11 SDK 10.0.26100 or newer installed.
- Solution platform set to `x64`.
- Startup project set to `SnapStudio.App`.

`SnapStudio.App` is an unpackaged WinUI app for normal local development. MSIX packaging is available through the explicit packaging script in `eng\package-msix.ps1`; see `docs\packaging.md`.

## CLI

Use the legacy solution file for local checks:

```powershell
dotnet restore SnapStudio.sln
dotnet format SnapStudio.sln --verify-no-changes --no-restore
dotnet build SnapStudio.sln --configuration Release --no-restore -p:Platform=x64
dotnet test SnapStudio.sln --configuration Release -p:Platform=x64
```

Capture host smoke check:

```powershell
dotnet run --project src\SnapStudio.CaptureHost\SnapStudio.CaptureHost.csproj --configuration Release
```

The capture host smoke should print the still-capture placeholder message and exit when no Windows capture adapter is configured.

Release package smoke check:

```powershell
.\eng\run-release-smoke.ps1 -Platform x64 -Configuration Release
```

The smoke builds the MSIX through `eng\package-msix.ps1`, verifies the unpacked package manifest and required payload, and confirms the bundled privacy notes are present. The default MSIX output is unsigned. Provide a signing certificate only when doing local install or release packaging.

Release readiness check:

```powershell
.\eng\run-release-readiness.ps1
```

To generate a reviewable release-readiness report:

```powershell
.\eng\run-release-readiness.ps1 -ReportPath artifacts\release\release-readiness.md
```

For a public Store release candidate, also require Partner Center identity and hosted listing URLs through environment variables:

```powershell
.\eng\run-release-readiness.ps1 -RequireStoreEnvironment -ReportPath artifacts\release\store-release-readiness.md
```

UI Automation smoke check:

```powershell
.\eng\run-ui-smoke.ps1 -Configuration Release -Platform x64
```

The UI smoke launches the desktop app and verifies that the main first-run/settings, capture, annotate, export, and history controls are visible to Windows UI Automation.

The Settings surface includes the local document storage backend selector. `File system` keeps editable document records as JSON sidecars, while `Database` stores editable document records in `snapstudio-documents.db` through the `CSharpDB` NuGet package. Backend changes are applied after restarting the app because storage services are created during startup.

Scrolling capture smoke check:

```powershell
.\eng\run-scrolling-smoke.ps1 -Configuration Release -Platform x64 -Launch
```

The scrolling smoke enables `V1.ScrollingCapture` with a restorable settings backup and verifies that the feature-gated Capture > Scrolling command is visible. For manual toggling, use Settings > Features, save, and restart SnapStudio. Use the scrolling capture checklist in `docs\project-plan.md` for the manual compatibility pass.

Screen recording smoke check:

```powershell
.\eng\run-recording-smoke.ps1 -Configuration Release -Platform x64 -Launch
```

The recording smoke enables `V1.ScreenRecording` with a restorable settings backup and verifies that the feature-gated Capture > Record Screen command and Recording audio selector are visible. For manual toggling, use Settings > Features, save, and restart SnapStudio. Use the screen recording checklist in `docs\project-plan.md` for the full start/stop, audio, and hardware pass.
