# SnapStudio

SnapStudio is a local-first Windows screen capture and image editing app built with .NET, WinUI, and Windows platform capture APIs.

It is designed for practical capture workflows: capture a screen region or target, annotate it, crop or resize it, manage it in a local workspace, and export or copy the result without sending capture data to a cloud service.

## Status

SnapStudio is in active development. The current codebase includes the main capture/editor workflow, local workspace, settings, packaging scripts, and feature-gated v1 lanes for OCR, scrolling capture, and screen recording.

## Highlights

- Region, picker/window, display, full-screen, and scrolling capture entry points.
- Adjustable region selection before capture.
- Local workspace with recent captures, thumbnails, search, filters, rename, duplicate, folder open, remove from history, and move to recycle bin.
- Editable annotations: rectangle, ellipse, line, arrow, text, highlight, and blur.
- Selection, move, resize, style controls, opacity, presets, undo, and redo.
- Crop and resize, including pixel or percentage resize with optional aspect-ratio lock.
- Export to PNG, JPEG, PDF, and source image.
- Optional OCR, scrolling capture, and screen recording lanes behind feature flags and readiness gates.
- Local settings, local diagnostics, crash recovery journal, and MSIX packaging support.

## Architecture

The project is structured around clear boundaries:

- `SnapStudio.App`: WinUI app shell and composition root.
- `SnapStudio.Core`: contracts, models, workflows, editor commands, settings, diagnostics, and domain logic.
- `SnapStudio.Platform.Windows`: Windows capture, OCR, clipboard, hotkey, recording, scrolling, and shell adapters.
- `SnapStudio.Storage`: local document, settings, diagnostics, OCR cache, and thumbnail persistence.
- `SnapStudio.Rendering`: document rendering and raster edits.
- `SnapStudio.Ipc`: editor message serialization and named-pipe transport.
- `SnapStudio.CaptureHost`: capture host boundary.
- `tests/SnapStudio.Core.Tests`: unit and integration-style coverage for core and platform-adapter behavior.

See [docs/project-plan.md](docs/project-plan.md) for the working roadmap, QA checklist, release gates, and architecture principles.

## Requirements

- Windows 11.
- .NET 10 SDK.
- Windows 11 SDK 10.0.26100 or newer.
- Visual Studio with Windows App SDK / WinUI tooling for IDE development.
- PowerShell for `eng` scripts.

## Getting Started

Clone the repository:

```powershell
git clone https://github.com/MaxAkbar/SnapStudio.git
cd SnapStudio
```

Restore, build, and test:

```powershell
dotnet restore SnapStudio.sln
dotnet build SnapStudio.sln --configuration Debug -p:Platform=x64
dotnet test SnapStudio.sln --configuration Debug -p:Platform=x64
```

Open `SnapStudio.sln` in Visual Studio, select the `x64` platform, and run `SnapStudio.App`.

More setup notes are in [docs/development-setup.md](docs/development-setup.md).

## Validation

Use these checks before committing app or tooling changes:

```powershell
dotnet format SnapStudio.sln
dotnet build SnapStudio.sln --no-restore -p:Platform=x64
dotnet test SnapStudio.sln --no-build -p:Platform=x64
.\eng\run-ui-smoke.ps1 -Configuration Debug -Platform x64 -SkipBuild
```

Release readiness checks:

```powershell
.\eng\run-release-smoke.ps1 -Platform x64 -Configuration Release
.\eng\run-release-readiness.ps1 -ReportPath artifacts\release\release-readiness.md
```

Feature-lane smoke checks:

```powershell
.\eng\run-ocr-smoke.ps1 -Platform x64 -Configuration Release -RegisterLayout -Launch -KeepAppOpen
.\eng\run-scrolling-smoke.ps1 -Configuration Release -Platform x64 -Launch
.\eng\run-recording-smoke.ps1 -Configuration Release -Platform x64 -Launch
```

## Packaging

Build an unsigned x64 MSIX package:

```powershell
.\eng\package-msix.ps1 -Platform x64 -Configuration Release
```

Packaging and Store submission details are documented in:

- [docs/packaging.md](docs/packaging.md)
- [docs/store-submission.md](docs/store-submission.md)

## Privacy

SnapStudio is local-first. It does not upload captures, recordings, documents, settings, logs, or exports to a SnapStudio service.

See [docs/privacy.md](docs/privacy.md) for the full privacy disclosure.

## License

SnapStudio is licensed under the [MIT License](LICENSE).
