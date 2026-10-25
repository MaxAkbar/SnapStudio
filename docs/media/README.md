# README demonstration

`snapstudio-demo.gif` is a short, looping walkthrough of the running Windows
application. `snapstudio-demo.png` is a still alternative. Both show the current
development interface, which is newer than the downloadable alpha preview.

The example starts with sample travel-site artwork already loaded in a separate
local workspace. The arrow, text note, repositioning, and final editor state
were captured from the app. Captions and a progress indicator were added during
GIF assembly; this is an edited walkthrough, not a real-time screen recording.
The final step shows the finished image and describes the available sharing
options; it does not show a completed export.

## Refreshing the demo

1. Launch a current build using an isolated `SNAPSTUDIO_DATA_ROOT` so personal
   captures and settings do not appear. Use sample artwork you can redistribute.
2. Keep the app window at the same size and scaling for every capture. Save five
   PNG screenshots in a working directory outside this documentation folder:
   `01-open.png`, `02-arrow.png`, `03-note.png`, `04-adjust.png`, and `05-share.png`.
3. Capture the sample image, an added arrow, a text note, the repositioned note,
   and the final document with its selection cleared. Keep private paths,
   personal images, dialogs, and notification banners out of the frames.
4. Assemble the assets using Python and Pillow:

   ```powershell
   python docs/media/build-demo.py --frames-dir artifacts/readme-demo/frames
   ```

5. Preview the animation and still, check file size and readability at README
   width, and verify the links in the root README. Update its development-build
   caption if the demo and published release now match.

The build script uses Segoe UI from Windows. It produces a 12.5-second GIF at
1,200 pixels wide with a shared color palette and a still PNG. Raw working
screenshots stay under the ignored `artifacts` directory; only the final media,
assembly script, and these notes are part of the repository.
