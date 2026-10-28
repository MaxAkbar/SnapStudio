# SVG tool designer

Open **Design → New SVG tool** for a transparent canvas, or choose **Design SVG**
in an existing tool's drawer to start from that tool's artwork. **Import SVG**
opens a local `.svg` in the same editor. Save the result into Custom shapes (K)
or an existing tool drawer. The drawer shows artwork previews; right-click a
preview to edit it, or to create an editable copy of a built-in design.

The designer supports rectangles, ellipses, lines (L), arrows (A), freehand, text, custom Bézier
paths, editable anchors and tangent handles, grouping, transforms, object order,
undo/redo, and direct SVG source editing. Text uses installed system fonts.
Stroke width and text size accept typed values without a slider cap. Use **Fit
artwork** to fit the SVG viewBox around the drawing, including its strokes.
Import and export in the editor operate on actual SVG files; library import and
export use SnapStudio JSON files containing both style presets and SVG designs.

In source builds, the main editor's **Line style → Custom SVG → Choose / design
SVG pattern…** opens the shared pattern library. Choose **New SVG pattern…** to
draw one repeat, name it, and save it. The motif repeats along a stroked shape
and rotates with curves, retaining its SVG colors. Line width controls motif
size; **Scale** and **Spacing** adjust the repeats. The SVG designer's **Custom
SVG** line style uses the same saved patterns. Existing objects keep their
embedded motif when a library pattern is edited. Choose Solid or another line
style to remove a custom pattern.

With a drawing tool active, hover over existing artwork to see its outline and move cursor. Click or drag to select and move it, then use its handles to resize or rotate. The current drawing tool stays active. Hold **Ctrl** to draw overlapping artwork instead. Shift-click still adds or removes objects from the selection; node editing and an unfinished Bézier path retain their own controls. Hover guides remain outside saved and exported artwork.

In source builds, open **Edit** in the toolbar, use **More object actions** above
the Objects list, or right-click artwork or an Objects row for all editing
commands. **Duplicate** and **Delete** also have buttons above the Objects list.
Right-clicking an object selects it; right-clicking a selected object keeps the
whole multiple selection. Right-clicking empty canvas keeps the selection too.
The menu includes cut/copy/paste, grouping, one-step or full stacking order,
rotation, horizontal/vertical flips, path nodes, Properties, and Guides.
Unavailable actions are disabled. **Shift+F10** opens the same menu from the
canvas or a focused Objects row; arrow keys navigate and Escape closes it.

**Ctrl+C**, **Ctrl+X**, and **Ctrl+V** copy, cut, and paste objects within the
current SVG editor session, including their embedded vector resources and custom
line patterns. They leave normal text editing alone in input fields and SVG
source. **Ctrl+D** duplicates with an offset. **Delete** removes selected objects
(or the active path anchor while editing nodes). **Ctrl+G** groups;
**Ctrl+Shift+G** ungroups. **Ctrl+[ / ]** moves backward/forward one step; add
**Shift** to send to the back or bring to the front. Each object edit supports
Undo/Redo. These additions are not in the 1.0.0 download.

Hold **Shift** while drawing a line or arrow with perspective snapping off,
moving objects, or rotating to snap the angle in **15° increments**.
Shift still makes rectangles square and ellipses
circular, and preserves proportions when resizing.

In source builds, **Snap** beside Grid aligns moving objects by their edges or
centers, including the SVG canvas edges and center. Resize corners also align
with nearby object edges and centers. Magenta guides show the match during the
drag. Groups and multiple selected objects use their combined bounds. Hold
**Alt** to bypass snapping, or turn **Snap** off for free object positioning.
The preference lasts while the designer is open and stays out of artwork and
undo history. With **Snap** on, **Snap to grid** adds alignment to visible grid
guides; the closest alignment wins. Turning **Snap** off also disables grid and
perspective snapping. Perspective-bound artwork continues to follow its guide family
while perspective snapping is active.

In source builds, **Perspective targets** in Properties limits the guide
families an object can snap to. Select an object, expand the control, choose
**None**, then enable the VP or VPs you want. **All guides** restores automatic
choice. Guides remain visible and object-to-object alignment stays available.
With nothing selected, these controls set the default for new drawings in the
current editor session. Existing objects retain their own choices.

Multiple selections show mixed checkboxes. Toggling one VP preserves each
object's other choices, and a combined move uses only targets allowed by every
selected object. Object choices support Undo/Redo and survive duplicate,
copy/paste, save, and reopen. The main canvas has the same control in Properties.
**Alt**, hiding the whole grid, and the master Snap switch still bypass perspective
snapping. Selecting **None** lets a bound perspective path move freely.

Perspective lines use their allowed directions even between visible guides;
changing targets takes effect on the next draw or move. In curved modes,
opposing poles share a geometric curve direction: Top/Bottom permit vertical
curves, and Left/Right permit horizontal curves in five-point mode. Alignment
to displayed guides still distinguishes each colored half. Perspective
rectangles retain the two axes of their selected plane; target choices control
guide alignment, while **None** disables their perspective drawing and movement.

In source builds, **Grid** shows or hides a square editing grid in the SVG
designer. Open **Grid settings** to adjust cell spacing from **4 to 512 SVG
coordinate units** and turn **Snap to grid** on or off. Fine grids use wider
visible spacing at low scale, and snapping uses those same lines. Hold **Alt**
to bypass grid snapping. Hiding the grid also stops grid snapping. The grid is
an editing guide and is excluded from saved and exported SVG artwork. These
controls are not included in the 1.0.0 download.

Grid visibility, spacing, and the snap preference are shared with the main
canvas for the current workspace session. In the designer, snapping applies to
object and group movement, resize corners, new shape endpoints, and path
anchors. Freehand strokes and Bézier control handles remain free to draw curves;
existing Shift constraints and Alt controls continue to work.

The **Guides** inspector contains icons for **Rectangular**, **1-point**,
**2-point**, **3-point**, **4-point curved**, and **5-point fisheye**. Perspective
modes use a separate density for each guide family. Adjust the horizon and the
displayed vanishing-point positions as percentages of the SVG viewBox; points
can sit outside the artwork. Three-point mode adds a third vertical vanishing
point. Four-point mode combines two horizontal vanishing points with curved
vertical guides; five-point mode curves both guide families and adds depth
lines through the center. The curve radius is a percentage of the shorter side.
**Reset perspective positions** in **More options** restores positions for the selected mode.

Open **Guides** beside **Properties**, use the Grid settings arrow, or right-click
the canvas and choose **Grid and perspective…**. The inspector stays beside the
canvas. **Shift+F10** also opens the canvas menu. Switch back to **Properties**
for ordinary object and layer editing.

The compact **Guide families** list shows each family's color, opacity, density,
and visibility. Select a row to expand one family editor. Type exact values or
drag sliders for that family's appearance and available VP coordinates. The eye
button hides a family and restores its previous opacity when shown again.
Rectangular mode uses cell spacing for its base grid.

With **Edit points on canvas** on, drag a VP or the horizon to reposition the
guides. Off-canvas VPs appear at the artwork edges; dragging changes their actual
position by the drag distance, without jumping to that edge. Use the coordinate
fields for exact values outside the artwork. Focus a handle and use arrow keys
to move it by one SVG unit (one image pixel on the main canvas), or **Shift** for
ten. **Escape** cancels the active drag. Curved poles share the curve radius,
so moving one updates its linked poles. Guide changes leave artwork and its
undo history untouched and remain excluded from exports.

| Mode | Independent families |
| --- | --- |
| 1-point | Base grid, VP 1 |
| 2-point | Base grid, Left VP, Right VP |
| 3-point | Base grid, Left VP, Right VP, Third VP |
| 4-point curved | Base grid, Left VP, Right VP, Top VP, Bottom VP |
| 5-point fisheye | Base grid, Left VP, Right VP, Top VP, Bottom VP, Center VP |

Curved guides split at the center: top and bottom halves use their respective
VP settings, as do the left and right halves in five-point mode. The base grid
is a separate reference family (vertical guides in two-point mode). Zero opacity
hides a family's guides and removes those guides from object snapping. Straight
and perspective drawing tools still follow the selected mode's directions.
Settings are shared with the canvas for the workspace session and stay out of
saved artwork and exports. **Reset guide families** restores colors, opacity,
and densities; **Reset perspective positions** resets positions while keeping family settings.

Perspective settings are shared with the canvas for the workspace session.
With perspective snapping on, the ordinary **Line and Arrow** tools follow a direction through
the exact point you click, even between visible guides. The initial drag
chooses the closest perspective direction and locks it until you finish the
stroke. Guide density changes the reference lines, not the available drawing
positions. Four- and five-point modes use the curve's local tangent or a
vanishing-point direction, keeping lines and arrows straight. Perspective
directions take priority over Shift's 15° angles. Turn off **Snap to grid** or
hold **Alt** to draw freely.

Choose **Perspective line** in Guides or its SVG toolbar icon to follow a
whole guide curve. On the main canvas, open the **Line** tool drawer and choose
**Perspective line**. Your click fixes the start, even between the visible
guides. The initial drag chooses a guide direction and locks it for the stroke;
guide density only changes the reference lines.

In one-, two-, and three-point modes, Perspective line follows straight
vanishing-point or parallel directions. Four-point mode also follows the full
vertical curves. Five-point mode follows horizontal or vertical curves, or a
straight depth line through the center. Curved strokes stop at the canvas or
curved-grid rim. Selecting the tool shows the grid and chooses four-point mode
if the grid was rectangular; existing perspective settings are retained.

Guides groups both perspective tools and the rectangle plane selector. Selecting
a perspective tool opens Guides with point editing off, so handles do not catch
drawing gestures. Open Guides manually or check **Edit points on canvas** when
you want to move the guides. The contextual toolbar provides stroke color and
width; rectangles also show their plane. The existing tool drawers remain
available on the main canvas.

Keep the grid and **Snap to grid** on, plus the main canvas **Snap** switch when
drawing there. Hold **Alt**, turn snapping off, or hide the grid to draw a free
straight line. Each stroke is an open SVG path with no fill, its own stroke
color, width, dash style, opacity, and one undo step. Choose **Edit SVG** on the
main canvas to edit its path. The ordinary Line and Arrow
tools continue to stay straight, using a curve's local direction when snapping.

**Perspective rectangle** is available in Guides and beside Rectangle in the SVG
toolbar. Choose **Front**, **Side**, or **Ground** in its contextual **Perspective plane**
selector, then drag opposite corners. Opposite edges follow the selected
plane's guide families even between visible guides; four- and five-point modes
use curved edges where the plane calls for them. The clicked corner stays
fixed. The face stops at the canvas boundary, vanishing axis, or curved-grid
rim. Start curved faces inside that rim. Selecting the tool shows the grid and
chooses two-point perspective if the current grid is rectangular. Existing
perspective settings and the snap preference are retained.

The main canvas offers the same tool in the **Rectangle** drawer: choose its
plane, then **Perspective rectangle**. It is also in the image background's
right-click tool menu. Front means width × height, Side means depth × height,
and Ground means width × depth. A one-point Front face is parallel to the
image, so it remains rectangular. With snapping off or **Alt** held, the tool
draws a flat face. The ordinary Rectangle tool keeps its usual behavior.
Each face is a single SVG object with its fill, stroke, and one undo step;
choose **Edit SVG** on the main canvas to edit its paths and colors.

With perspective snapping on, moving a **Perspective line** or **Perspective
rectangle** recalculates its edges at the new position against the active grid.
Dragging, arrow-key nudges, and SVG X/Y position changes retain the line's guide
family or the rectangle's selected plane, its appearance, and one undo step per
move. Hold **Alt**, turn snapping off, or hide the grid for a free move; the next
move with perspective snapping on fits the object to the grid again. Changing
grid settings leaves existing objects in place until their next snapped move.

Resizing and rotating still transform the saved artwork. Editing a path's
geometry can detach its perspective behavior so a later move preserves your
custom edits; changing colors, stroke width, dashes, or opacity retains it.
Ordinary shapes and custom SVG artwork keep their normal movement behavior.

Other objects and resize corners snap to nearby visible segments and curves,
preserving Shift constraints and one undo step per completed drag. These are drawing guides:
positioning an SVG object on a guide keeps its artwork intact rather than
applying perspective distortion. The guides are excluded from saved SVGs and
exports and remain isolated from styles inside imported artwork.

On the main canvas, drawing an SVG tool object also snaps its start and end
corners to the perspective guides. The ordinary native Line and Arrow tools use the same
continuous direction snapping as the SVG editor. Native shapes, text,
toolbar object insertion, and stamp placement use nearby perspective guides.
Hold **Alt** while drawing or placing a stamp for free positioning. The native
canvas keeps each completed drawing or stamp placement as one undo step.

Drawing an SVG variant places one SVG object, with its own saved source. Select
that object and choose **Edit SVG** in Properties or its context menu. Changes
apply to that object and can be undone; its reusable tool and duplicates keep
their own artwork. Individual fills, strokes, paths and text inside an SVG are
edited in the SVG designer. Existing native shape tools keep their usual shape
properties and fill-bucket behavior.

Documents store SVG source and layer clipping in schema 9; earlier documents migrate on load. Tool
libraries use schema 2 and still read schema 1 style libraries. The canvas uses
a cached transparent preview, while image exports render the saved SVG at the
output size. Moving an unchanged object reuses its preview.

This version accepts static vector SVG: paths, shapes, text, groups, transforms,
gradients and internal references. Scripts, external resources, raster image
elements, filters, animation and foreign HTML are rejected. SVGs are limited to
1 million characters, 4096 elements and 32 nesting levels. Paths containing arc
commands can be transformed and edited in source; visual node editing currently
supports lines and Bézier curves. Font appearance depends on installed fonts.

The editor runs from bundled local assets in WebView2 and makes no network
requests. Imported SVG styles are isolated from the editor UI in a shadow root.
SVG.NET renders previews and exports; its notices are in `third-party/`.
