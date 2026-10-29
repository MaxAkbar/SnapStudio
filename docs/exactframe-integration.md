# Capture from ExactFrame

Use [ExactFrame](https://github.com/MaxAkbar/ExactFrame) to position a region, then open a still capture of that region in SnapStudio for annotation, copy, or export.

## Set up

This feature requires **integration-enabled builds of both apps**. The published SnapStudio **1.1.0** and ExactFrame **2.1.1** packages do not support it. Build both projects from source. Keep the entire SnapStudio output folder together, including `SnapStudio.integration.json`.

1. Open SnapStudio and finish first-run setup. Keep still capture enabled.
2. In ExactFrame, select **Outline area** and position the main frame. You can also add a nested recording guide.
3. Under **Capture in SnapStudio**, choose the main frame or a guide, then click **Capture in SnapStudio**.
4. On first use, choose **SnapStudio.App.exe**. ExactFrame remembers the path. Use **Choose app…** if you move or replace that build.

SnapStudio starts automatically when needed. ExactFrame hides its windows, HUD, and overlays during capture. SnapStudio minimizes, captures the region, saves it to its own local library, and opens it for editing. ExactFrame restores its visible windows without taking focus away from SnapStudio. Capturing does not change the clipboard.

## Geometry and limits

- Coordinates and dimensions are **physical screen pixels**, including on displays with 125%, 150%, or other scaling. Negative desktop coordinates are supported.
- An additional guide uses its **on-screen crop**, without scaling to its planned recording output size.
- The full region must fit on one connected display. A spanning or off-screen region is rejected rather than clipped.
- Each side must be 4–20,000 pixels, and the image must contain at most 40 million pixels.
- Finish modal dialogs or active edits in SnapStudio before capturing. Capture is unavailable while SnapStudio is already capturing or recording.
- Close an older running SnapStudio copy before starting the integration-enabled build. Both apps should run with matching permissions in the same Windows session.

If startup takes over 20 seconds, open SnapStudio, finish setup, and retry. A capture has a bounded deadline; disconnecting or closing either app cancels the request and ExactFrame restores its windows. Display changes can require repositioning the frame and retrying.

## Local protocol v1

This integration uses a dedicated, current-user-only duplex Windows named pipe. It is separate from SnapStudio's existing editor notification channel. The default name is `SnapStudio.ExternalCapture.v1.Session{WindowsSessionId}`. Both apps may set `SNAPSTUDIO_INTEGRATION_PIPE` to the same unique name for isolated development checks.

Messages are UTF-8 JSON followed by a newline, capped at 8,192 bytes. One connection handles one request and response. The server deadline is 50 seconds after connection; the client allows 60 seconds for capture after connecting. A client disconnect cancels the in-flight operation.

Request:

```json
{"protocolVersion":1,"requestId":"2a4de139-078e-46bd-9e10-b1886c003354","x":-1920,"y":0,"width":1280,"height":720,"name":"Main frame"}
```

Success response:

```json
{"protocolVersion":1,"requestId":"2a4de139-078e-46bd-9e10-b1886c003354","succeeded":true,"error":null,"documentId":"d81c2503-a71a-4b3d-aa26-179706506330"}
```

Failure responses retain the request ID when readable, set `succeeded` to `false`, and include a user-readable `error`. The client checks the version and correlation ID before accepting success. Invalid input is rejected without stopping the listener.

Only bounds, a frame name, correlation IDs, and status cross the pipe. SnapStudio performs the capture and owns its resulting document. The apps do not share databases, exchange temporary image files, or send this data online. This same-user channel is not an authentication boundary between programs running as the same Windows user.

## Development checks

Build and run the automated tests in each repository. SnapStudio covers bounds validation, monitor containment, request recovery, bounded messages, and disconnect cancellation. ExactFrame covers frame choices, physical dimensions, app selection, persistence, errors, and protocol responses. Native checks are still needed for Windows capture, overlay visibility, focus, and display scaling.

Local Windows checks exercised a 640 × 360 main frame and a 189 × 336 nested guide on a display at 175% scaling. Both opened at the exact physical dimensions, with known fixture pixels and no app windows or guide lines in the saved image. Cold startup, reuse of an open SnapStudio, workspace focus, restored outlines, and rejection while a New image dialog was open were checked. The first-use file picker was opened; file selection itself was covered through service tests. Other native display layouts, negative-coordinate monitors, ARM64, and packaged MSIX activation remain unverified for this integration.

For isolated native checks, set `SNAPSTUDIO_DATA_ROOT` and `EXACTFRAME_DATA_ROOT` to separate disposable directories before starting their respective apps. ExactFrame still allows only one instance per Windows session. Set the same unique `SNAPSTUDIO_INTEGRATION_PIPE` in both processes so the check cannot target another SnapStudio instance.
