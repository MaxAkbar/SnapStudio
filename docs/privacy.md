# SnapStudio Privacy Disclosure

Status: v1 hardening disclosure baseline.

SnapStudio is designed as a local-first screen capture, image editor, and feature-gated recording tool. The app does not upload captures, recordings, documents, settings, logs, or exports to a SnapStudio service.

## Local Capture Data

Screen captures and imported images are stored locally under `%LOCALAPPDATA%\SnapStudio\Documents` unless the user chooses another storage location in Settings.

Each saved capture or imported image can include:

- The source image file.
- A JSON sidecar with editable annotation metadata.
- Cached thumbnails.
- Local document metadata such as title, created time, modified time, source type, annotation count, and source dimensions.
- Optional OCR cache files when `V1.Ocr` is enabled.

Deleting a capture from SnapStudio deletes the local document folder for that capture.

## Clipboard, Export, And External Apps

Captures stay local to SnapStudio unless the user explicitly:

- Copies a capture to the clipboard.
- Exports PNG, JPEG, or PDF.
- Opens the containing folder.
- Imports or opens the file in another application.

After a capture is copied, exported, or opened outside SnapStudio, the destination application or folder controls that copy.

## Settings

Settings are stored locally in `%LOCALAPPDATA%\SnapStudio\settings.json`.

Settings can include:

- Storage location.
- Capture hotkey.
- Cursor capture preference.
- Clipboard preference.
- First-run completion state.
- Local feature flags.

Settings migration preserves user choices and updates the local schema version when the app changes its settings format.

Settings can be exported to or imported from a user-selected `.json` file. Exported settings are controlled by the destination folder or app after export. Imported storage-root and feature-flag changes may require restart because capture, OCR, scrolling, and recording services are composed at startup.

## Diagnostics And Recovery Logs

SnapStudio writes local diagnostic logs under `%LOCALAPPDATA%\SnapStudio\Logs`.

Current logs include:

- `snapstudio.jsonl`: normal diagnostics such as shell startup, capture success, and capture cancellation.
- `recovery.jsonl`: crash recovery checkpoints such as session start, session close, document opened, document saved, document cleared, and unhandled exception summaries.

The logs redact Windows paths, email-like values, and URLs where logged through the diagnostic and recovery pipelines. Logs are not uploaded automatically. Diagnostics can still include non-path operational metadata such as feature flag names, target kind, source dimensions, frame counts, recording audio mode, and failure reasons.

Scrolling capture diagnostics are local-only and remain behind the disabled `V1.ScrollingCapture` feature flag. When enabled, failed or partial scrolling captures can write a JSON diagnostics bundle beside the scrolling capture output. Bundle content redacts sensitive paths, email-like values, and URLs before writing and is not uploaded automatically.

## OCR

OCR remains behind the disabled `V1.Ocr` feature flag until packaged-build validation and privacy review pass.

OCR is local-first. When OCR is enabled, recognized text can be cached locally beside the capture document in `ocr-cache.snapstudio.json`; deleting the capture deletes that local OCR cache with the document folder.

## Screen Recording And Audio

Screen recording remains behind the disabled `V1.ScreenRecording` feature flag until manual hardware QA passes.

When enabled, screen recordings are saved locally as MP4 files under the configured storage root's `Recordings` folder. A recording can include no audio, microphone audio, or system audio. Recording both microphone and system audio at the same time is not implemented. Webcam capture is not implemented.

Recording diagnostics are local-only and can include target kind, output dimensions, frame counts, requested audio mode, audio format metadata, and failure reasons. They should not include raw audio or video content.

## Network And Cloud Behavior

SnapStudio has no cloud account, sync, team sharing, telemetry upload, crash upload, or remote capture processing.

Any future networked feature should require an updated privacy review and user-facing disclosure before release.

## User Controls

Users can:

- Choose the local storage location.
- Import and export local settings.
- Delete captures from the workspace.
- Delete `%LOCALAPPDATA%\SnapStudio` to remove local app data.
- Avoid clipboard or export actions when they do not want copies outside SnapStudio.
- Keep feature-gated OCR, scrolling capture, and recording disabled until they are ready for their workflow.
