# SnapStudio Project Plan

This is the working plan for SnapStudio. It replaces the older phase-by-phase notes and is intended to stay useful as a compact source of truth for planning, manual QA, release readiness, and next-step decisions.

## How To Use This Plan

- Keep active work in the "Current Work" section.
- Move completed work into the status snapshot only when it is implemented, validated, and committed.
- Track release-blocking issues in the "Known Issue Triage" section until they are fixed or explicitly deferred.
- Use the QA checklists before declaring a feature lane ready for alpha, beta, or Store release.
- Keep detailed setup, packaging, privacy, and Store instructions in their dedicated docs:
  - `docs/development-setup.md`
  - `docs/packaging.md`
  - `docs/privacy.md`
  - `docs/store-submission.md`

## Product Direction

SnapStudio is a local-first Windows screen capture and image editing app for professional capture workflows.

The product goal is a Snagit-like capture, editor, workspace, and export tool. It should feel practical and fast for repeated work: capture, annotate, crop, resize, copy, export, reopen, and manage captures without losing editable state.

The product is not automation-first like ShareX, and it is not cloud-first. Networked, team, AI, or plugin workflows should remain optional future capabilities and require separate privacy review before release.

## Architecture Principles

Implementation should follow SOLID principles and keep platform concerns isolated.

- Single responsibility: capture, rendering, document storage, editor commands, settings, diagnostics, packaging, and Windows interop stay in separate services or projects.
- Open/closed: new capture targets, export formats, OCR providers, recording engines, scrolling strategies, and storage backends should be added behind existing interfaces where practical.
- Liskov substitution: platform fallback implementations must behave predictably through the same contracts as full Windows implementations.
- Interface segregation: UI code should depend on focused app-facing services rather than large platform adapters.
- Dependency inversion: core workflows depend on contracts; Windows APIs live in `SnapStudio.Platform.Windows`, storage lives in `SnapStudio.Storage`, rendering lives in `SnapStudio.Rendering`, and UI composition lives in `SnapStudio.App`.

Document and editor state should preserve a source raster plus editable annotations. Destructive raster edits, such as crop and resize, should remain explicit operations with undo/redo support.

## Current Status Snapshot

The current app has one clean root commit after history squash: `Implement SnapStudio application`.

Implemented foundations:

- .NET solution with app, capture host, core contracts, platform adapters, storage, rendering, IPC, and tests.
- WinUI app shell with capture, editor, history, settings, and status surfaces.
- Local settings store, import/export settings, feature flags, diagnostics, and crash recovery journal.
- Document repository with selectable file-system or CSharpDB-backed editable document records, source images, thumbnails, metadata, and recent history.
- Named-pipe editor message infrastructure for host/editor separation.

Implemented capture and workspace:

- Region, picker/window, display, full-screen, and scrolling capture entry points.
- Adjustable region selection before capture.
- Delayed capture, cursor preference, clipboard copy, and export actions.
- Open image import.
- Recent capture history with thumbnails, search, source filters, rename, duplicate, open containing folder, remove from history, and move source image to recycle bin.
- Selected capture property display for width, height, and file size.
- Pin-to-screen support.

Implemented editor:

- Canvas display with zoom, fit, actual size, and annotation overlays.
- Annotation tools for rectangle, ellipse, line, arrow, text, highlight, and blur.
- Selection, move, resize, delete, style controls, opacity, presets, undo, and redo.
- Crop to selected annotation.
- Resize by pixels or percent.
- Aspect-ratio lock for proportional resizing.
- PNG, JPEG, PDF, and source-image export paths.

Implemented v1 lanes behind feature flags or readiness gates:

- OCR provider abstraction, Windows OCR path, selected-region OCR, copy recognized text, and OCR cache.
- Scrolling capture detection, orchestrator, frame capture, stitching, partial-success handling, and diagnostics bundle.
- Screen recording command path, MP4 output, microphone/system audio mode selection, and recording diagnostics.

Implemented release path:

- MSIX packaging script.
- Release package smoke.
- Release readiness script.
- Microsoft Store submission guidance.
- Local-first privacy disclosure.

## Current Work

Near-term work should focus on making the current feature set reliable, easy to test, and release-ready.

1. Verify right-side editor controls at small window sizes.
2. Verify resize behavior:
   - Pixel resize with aspect ratio locked.
   - Pixel resize with aspect ratio free.
   - Percent resize with aspect ratio locked.
   - Percent resize with aspect ratio free.
   - Resize undo/redo.
   - Reopen after resize.
3. Run capture regression on region, picker/window, display, full-screen, and open image.
4. Run workspace regression on thumbnails, selected capture properties, rename, duplicate, folder open, remove from history, and move to recycle bin.
5. Run release readiness after the documentation consolidation.

## Roadmap

### Lane A: MVP Stabilization

Goal: make still capture, editing, workspace, settings, and export stable enough for daily use.

Scope:

- Polish right-panel controls and dense layouts.
- Verify capture and editor workflows on small, normal, and high-DPI displays.
- Fix data-loss, thumbnail, or document reopen issues immediately.
- Keep settings fully UI-controlled wherever practical.
- Keep storage backend selection in Settings and apply backend changes through startup composition.
- Keep logs redacted and local.

Exit criteria:

- No known data-loss issues.
- No known capture blocker for region, picker/window, display, or full-screen capture.
- Workspace history and thumbnails update reliably.
- Resize, crop, annotations, undo/redo, export, reopen, and delete flows pass manual QA.

### Lane B: OCR Readiness

Goal: validate OCR as a packaged-build feature.

Scope:

- Package and register a local MSIX layout.
- Enable `V1.Ocr`.
- Confirm OCR command visibility.
- Test selected-region OCR and full-image OCR fallback.
- Confirm OCR cache is local and deleted with the document.

Exit criteria:

- OCR works in a packaged x64 build.
- OCR failures show a recoverable message and do not affect document editing.
- Privacy disclosure remains accurate.

### Lane C: Scrolling Capture Readiness

Goal: validate scrolling capture on a curated compatibility set.

Scope:

- Browser pages with static and sticky headers.
- Long documents in common document viewers.
- Win32 scrollable content.
- Mixed-DPI and high-resolution displays.
- Partial-success handling and diagnostics bundle review.

Exit criteria:

- Successful full captures on the supported target set.
- Partial captures preserve useful output and show a clear warning.
- Diagnostics are local and redacted.

### Lane D: Screen Recording Readiness

Goal: validate screen recording before exposing it broadly.

Scope:

- No-audio MP4.
- Microphone MP4.
- System-audio MP4.
- Recording indicator.
- Stop workflow.
- Hardware matrix and sync drift checks.

Exit criteria:

- MP4 output plays in common players.
- Audio mode behavior is clear.
- Failures do not crash the app or lose still-capture work.

### Lane E: Store Release Candidate

Goal: prepare a repeatable public package path.

Scope:

- Partner Center app reservation.
- Store package identity values.
- Hosted privacy policy URL.
- Hosted support URL.
- Store listing content and screenshots.
- Store-identity package smoke.
- Release readiness report.

Exit criteria:

- `eng\run-release-readiness.ps1 -RequireStoreEnvironment` passes.
- `eng\run-release-smoke.ps1 -RequireStoreIdentity` passes.
- Known issues are triaged as release blocker, deferred, or accepted.

### Lane F: v2 Candidates

Do not start these until v1 release risks are under control.

Candidates:

- Smart redaction.
- Object-aware edits.
- Templates and documentation output.
- Plugin SDK.
- Webcam/PiP recording.
- GIF or lightweight clip editing.
- Optional integrations.

## QA Checklists

### Still Capture And Editor

- Region capture shows visible content during selection.
- Adjustable region rectangle can move and resize before confirming.
- Picker/window capture handles minimized or unavailable windows with a clear error.
- Display and full-screen capture produce editable documents.
- Open image imports a document and selects it in history.
- Copy current capture places the image on the clipboard.
- Export PNG, JPEG, and PDF succeeds.
- Annotations can be added, selected, moved, resized, styled, deleted, undone, and redone.
- Blur and highlight remain visually distinct from stroke color tools.
- Crop and resize work and can be undone.
- Reopened documents preserve annotations and destructive edits.

### Workspace

- New captures appear in history immediately.
- Thumbnails update after annotations and destructive edits.
- Multiple annotations appear in thumbnails.
- Search and source filters find expected captures.
- Rename updates history and canvas title.
- Duplicate creates a separate editable document.
- Open containing folder selects or opens the current source location when possible.
- Remove from history keeps the source image file.
- Move to recycle bin removes history and moves the source image to the Windows recycle bin.
- Selected capture properties show width, height, and file size.

### Resize

- Pixel mode shows readable width and height values.
- Percent mode switches labels to percent.
- Switching between pixel and percent preserves the current target size.
- Locked aspect ratio changes the paired field.
- Free aspect ratio allows independent width and height changes.
- Very large percentages are capped by the 20,000px dimension ceiling.
- Resize command writes the expected image dimensions.

### OCR

- Run `eng\run-ocr-smoke.ps1 -Platform x64 -Configuration Release -RegisterLayout -Launch -KeepAppOpen`.
- Enable OCR through Settings when testing manually.
- Confirm Copy Text is visible only when OCR is enabled.
- Confirm selected-region OCR copies expected text.
- Confirm no document data is uploaded.

### Scrolling Capture

- Run `eng\run-scrolling-smoke.ps1 -Configuration Release -Platform x64 -Launch`.
- Test at least one browser page, one document viewer, and one Win32 scrollable target.
- Confirm full capture opens as a document.
- Confirm partial capture opens when possible and reports the limitation.
- Confirm diagnostics bundle paths are local and redacted.

### Screen Recording

- Run `eng\run-recording-smoke.ps1 -Configuration Release -Platform x64 -Launch`.
- Test no audio, microphone, and system audio.
- Confirm recording indicator appears while recording.
- Confirm Stop creates an MP4 under the configured storage root.
- Confirm failed recordings do not corrupt still-capture documents.

### Packaging And Store

- Run `eng\run-release-smoke.ps1 -Platform x64 -Configuration Release`.
- Run `eng\run-release-readiness.ps1 -ReportPath artifacts\release\release-readiness.md`.
- For public Store release, set Partner Center identity and hosted URL environment variables, then run readiness with `-RequireStoreEnvironment`.
- Build the Store-identity package with `-RequireStoreIdentity`.
- Confirm `docs/privacy.md` is still accurate before publishing a hosted policy.

## Known Issue Triage

Use these levels in every release readiness pass:

- Release blocker: data loss, capture corruption, privacy disclosure gap, installer failure, Store identity failure, or a high-frequency crash.
- Alpha blocker: prevents a core tester workflow but has no data-loss or privacy impact.
- Deferred: known limitation that is documented and does not block the target release.
- Accepted: behavior is intentional or outside current scope.

Current known issues:

- Public Store v1 still depends on external Partner Center identity values and hosted URLs.
- Final public-release manual QA evidence must be recorded for OCR, scrolling capture, screen recording, and Store packaging before submission.

## Privacy Release Check

SnapStudio has no network, telemetry, crash-upload, or cloud capture-processing service.

Before release:

- Confirm screenshots, recordings, OCR text, thumbnails, settings, exports, logs, and diagnostics stay local unless the user explicitly copies, exports, or opens them externally.
- Confirm diagnostics redact Windows paths, email-like values, and URLs.
- Confirm feature-gated OCR, scrolling capture, and recording disclosures in `docs/privacy.md` match the app.
- Confirm Store listing and hosted privacy policy match `docs/privacy.md`.

## Release Gates

| Gate | Required evidence |
|---|---|
| Local dev ready | Build, tests, and UI smoke pass |
| Private alpha | Still capture, editor, workspace, settings, export, and recovery QA pass |
| v1 beta | OCR, scrolling, and recording lanes pass their smoke tests and manual QA |
| Store candidate | Release smoke, release readiness, Store identity, hosted URLs, and privacy review pass |
| Public v1 | No release blockers remain and Partner Center package validation passes |

## Standard Validation Commands

Use these before committing app or script changes:

```powershell
dotnet format SnapStudio.sln
dotnet build SnapStudio.sln --no-restore -p:Platform=x64
dotnet test SnapStudio.sln --no-build -p:Platform=x64
.\eng\run-ui-smoke.ps1 -Configuration Debug -Platform x64 -SkipBuild
```

Use release checks before package handoff:

```powershell
.\eng\run-release-smoke.ps1 -Platform x64 -Configuration Release
.\eng\run-release-readiness.ps1 -ReportPath artifacts\release\release-readiness.md
```
