# SnapStudio

SnapStudio is a local-first Windows screen capture and image editing app built with .NET, WinUI, and Windows platform capture APIs.

It is designed for practical capture workflows: capture a screen region or target, annotate it, crop or resize it, manage it in a local workspace, and export or copy the result without sending capture data to a cloud service.

## Status

SnapStudio is in active development. The current codebase includes the main capture/editor workflow, local workspace, settings, packaging scripts, and feature-gated v1 lanes for OCR, scrolling capture, and screen recording.

## Highlights

- Region, picker/window, display, full-screen, and scrolling capture entry points.
- Adjustable region selection before capture.
- **New capture → Display** lets you choose a connected monitor. **Full Screen (all displays)** captures the complete desktop in one image, preserving monitor positions. **Window** provides a searchable, refreshable list of open windows.
- With **Include cursor** enabled, still captures keep the screenshot clean and add the visible pointer as an image object in a **Mouse pointer** layer. Select it to move or resize it, or hide/delete it independently. The pointer stays editable after saving and reopening; clipboard copies and exports include visible objects.
- Local workspace with capture history in pages of ten, thumbnails, search, filters, rename, duplicate, folder open, remove from history, and move to recycle bin. Use the arrows below the history to browse pages; search covers every page in the selected view, and new captures return to page 1.
- Tag images from **right-click → Edit tags…**, the current capture's **…** menu, or **Image details → Edit…**. Enter tags separated by commas or new lines. Use **Find or add a tag** to search the saved tag library by keyword and select a suggestion, or type a new tag and press **Add** or **Enter**. Search ignores case and excludes tags already assigned to the capture. There is no tag count limit; each label can have up to 64 characters. Remove labels from the field and save to untag that capture. Library search includes tags; **#Design** matches that exact tag, ignoring case, within the selected folder and source filter. Tags stay with each editable capture across restarts; duplicates inherit tags and can change them independently. Tagging leaves source image files untouched.
- The **Library** keeps folders, search, and paged captures together on the left. Choose **All captures**, **Unfiled**, or a folder from the compact dropdown. The **+** button beside it maps another folder; the adjacent **Refresh** button adds newly discovered images to the selected mapped folder, or checks every mapping when All captures or Unfiled is selected. SnapStudio keeps editable copies of PNG, JPEG, and BMP images and remembers the source location, optionally including subfolders. Existing copies and edits stay intact, even if an original is changed or removed. Already copied files are not added again. The **…** button beside **Captures** offers **New group…**, a one-time import, renaming, and removing a group. Right-click a capture to **Rename…**, **Duplicate**, **Move to folder**, **Show in folder**, **Remove from recent captures** while keeping its image file, or **Delete image (Recycle Bin)…** to recycle the file after confirmation. These actions target the clicked capture. Duplicates retain their editable layers and group; select the new copy to edit it. Renaming, duplicating, moving, or revealing another capture preserves the open editor, selection, zoom, and undo. Show in folder reveals the stored source image; for imports, this is SnapStudio's editable copy. Removing a group keeps its copies and returns them to Unfiled. Folder mappings and membership are saved in the workspace database.
- Editing tools sit beside the canvas. **Properties** and **Layers** share the right **Inspector**, with **Image details** and **Capture options** available when needed. Use **…** beside the image title to rename, duplicate, move to a folder, or remove the current capture. Collapse **Folders and search**, **Captures**, or **Layers** independently; collapsing Captures hides its list and pagination together. Both side panes can still be resized or collapsed. **Focus mode** beside the image title, or **F8**, hides both panes and restores their previous widths and open/closed states when you return.
- With the folder dropdown open, right-click a folder or group for **Rename…** and **Remove … (keep captures)**. Removing a mapped folder stops its mapping; removing either kind returns its captures to **Unfiled** and keeps the image files and editable documents. **All captures** and **Unfiled** remain available.
- Choose **Dark**, **Light**, or **Use system setting** under **Settings → Appearance → Theme**, then **Save** to apply it without restarting. The choice is remembered and included in settings imports and exports. Existing settings keep the dark theme.
- Editable annotations: rectangle, ellipse, line, arrow, text, freehand pen, highlight, and blur. Use the dashed-square Selection tool to select, move, and resize objects.
- Right-click the image background with no objects selected to choose **Select**, **Rectangle**, **Ellipse**, **Line**, **Arrow**, **Text**, **Pen**, **Highlight**, **Blur**, or **Fill bucket**. The menu marks the active tool and switches tools without adding an object. **Shift+F10** opens the same menu when the canvas has focus and no object is selected. Right-click an object for its existing editing actions.
- Right-click a layer to rename it, move it up or down in the stack, or choose a color label. **Custom color…** opens a color picker with a hex entry. Layer changes support undo and redo.
- Right-click an object inside a layer to move it up or down within that layer. Object order is saved and supports undo and redo.
- Right-click an object on the editor canvas or in the layer list to duplicate, show/hide, arrange, move to another visible layer, or delete it. Keyboard shortcuts are shown in the menu.
- Use **Fill bucket** inside a rectangle or ellipse to change that object's fill while keeping its outline. The fill moves and resizes with the shape. On image areas, the bucket adds a connected fill object to the selected layer; adjust tolerance to include similar colors. Both support undo and redo.
- Use **Copy** or **Ctrl+C** to copy the area inside the selection rectangle, including the visible image and objects. With nothing selected, Copy uses the whole edited image. **Paste** or **Ctrl+V** offers **New layer** to paste over the open image, or **New image** to open a separate document. A pasted layer keeps its transparency and can be moved or resized independently. The arrow beside Paste provides both actions directly; **Ctrl+Shift+V** opens a new image directly. With no image open, **Ctrl+V** creates a new document.
- Create a transparent canvas with **New** or **Ctrl+N**, then choose its width and height. The checkerboard shows transparency in the editor; PNG export preserves it.
- Selection, move, resize, style controls, opacity, presets, undo, and redo.
- Text boxes fit their content. Resize handles scale the text instead of cropping it, and text or size edits refit the box. Choose an installed system font under **Font** for new or selected text; the choice is saved and used in Copy and export. Type sizes beyond the slider range when needed. Copying selected text preserves the visible text and transparency.
- With the selection tool, drag a dotted rectangle to select every visible object it overlaps. **Shift-click** or **Ctrl-click** toggles individual objects; **Shift-drag** adds to the selection, **Ctrl-drag** toggles overlapping objects, **Ctrl+A** selects all visible objects, and **Escape** clears the selection or cancels a drag. Drag a selected object to move the selection together. Keyboard movement, duplicate, delete, visibility, style, arrange, and move-to-layer actions apply to the selection with one undo step. Select one object to use its resize handles.
- Hold **Ctrl** and scroll the mouse wheel over the editor to zoom in or out at the pointer. Scrolling without Ctrl keeps the normal canvas scrolling behavior.
- The dotted selection rectangle stays visible after drawing. Use **Crop** to keep that exact area, including padding around multiple selected objects. The rectangle follows group moves and stays aligned while zooming; cropping keeps the objects inside the area and can be undone.
- Crop and resize, including pixel or percentage resize with optional aspect-ratio lock.
- Export to PNG, JPEG, PDF, and source image.
- Optional OCR, scrolling capture, and screen recording lanes behind feature flags and readiness gates.
- Local settings, selectable file-system or CSharpDB document storage, local diagnostics, crash recovery journal, and MSIX packaging support.

## Architecture

The project is structured around clear boundaries:

- `SnapStudio.App`: WinUI app shell and composition root.
- `SnapStudio.Core`: contracts, models, workflows, editor commands, settings, diagnostics, and domain logic.
- `SnapStudio.Platform.Windows`: Windows capture, OCR, clipboard, hotkey, recording, scrolling, and shell adapters.
- `SnapStudio.Storage`: file-system and CSharpDB document storage, settings, diagnostics, OCR cache, and thumbnail persistence.
- `SnapStudio.Rendering`: document rendering and raster edits.
- `SnapStudio.Ipc`: editor message serialization and named-pipe transport.
- `SnapStudio.CaptureHost`: capture host boundary.
- `tests/SnapStudio.Core.Tests`: unit and integration-style coverage for core and platform-adapter behavior.

See [docs/project-plan.md](docs/project-plan.md) for the working roadmap, QA checklist, release gates, and architecture principles.

## Download the preview

Get the first preview from [GitHub Releases](https://github.com/MaxAkbar/SnapStudio/releases/tag/v0.1.0-alpha.1).
On Windows 11 x64, download `SnapStudio-0.1.0-alpha.1-win-x64-portable.zip`, extract the entire ZIP, and run `SnapStudio.App.exe`. The portable build includes .NET and the Windows App SDK runtime. Captures and settings are saved under your local Windows profile.

This is an unsigned alpha preview. OCR, scrolling capture, and screen recording are disabled by default. See the [release notes](docs/releases/v0.1.0-alpha.1.md) for validation and package limitations.

## Object shortcuts

Select an object on the canvas or in the layer list first. Text fields keep their normal editing keys.

| Shortcut | Action |
| --- | --- |
| Arrow keys | Move by 1 image pixel |
| Shift+Arrow | Move by 10 image pixels |
| Ctrl+D | Duplicate |
| Ctrl+H | Hide or show |
| Delete | Delete the selected object |
| Ctrl+Page Up / Page Down | Bring forward / send backward within its layer |
| Ctrl+Shift+Page Up / Page Down | Bring to front / send to back within its layer |
| Shift+F10 or Menu key | Open the selected object's context menu |
| Escape | Close the menu, then clear the selection |
| Ctrl+Z / Ctrl+Y | Undo / redo |

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

## Local Storage

SnapStudio stores data under `%LOCALAPPDATA%\SnapStudio\Documents` by default, or under the storage folder chosen in Settings.

The Settings window lets you choose the document storage backend:

- `File system`: editable document metadata is saved as per-capture JSON sidecars.
- `Database`: editable document metadata is saved in a local CSharpDB database file named `snapstudio-documents.db`.

Source images, thumbnails, OCR cache files, recordings, diagnostics, and exports remain local files under the configured storage root. Changing the storage location, backend, or feature gates is saved immediately and takes effect after restarting SnapStudio.

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
