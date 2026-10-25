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
Hold **Shift** while drawing a line or arrow with perspective snapping off,
moving objects, or rotating to snap the angle in **15° increments**.
Shift still makes rectangles square and ellipses
circular, and preserves proportions when resizing.

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

The same **Grid settings** contain icons for **Rectangular**, **1-point**,
**2-point**, **3-point**, **4-point curved**, and **5-point fisheye**. Perspective
modes replace cell spacing with **Guide density**. Adjust the horizon and the
displayed vanishing-point positions as percentages of the SVG viewBox; points
can sit outside the artwork. Three-point mode adds a third vertical vanishing
point. Four-point mode combines two horizontal vanishing points with curved
vertical guides; five-point mode curves both guide families and adds depth
lines through the center. The curve radius is a percentage of the shorter side.
**Reset perspective** restores defaults for the selected mode.

Perspective settings are shared with the canvas for the workspace session.
With perspective snapping on, the ordinary **Line and Arrow** tools follow a direction through
the exact point you click**, even between visible guides. The initial drag
chooses the closest perspective direction and locks it until you finish the
stroke. Guide density changes the reference lines, not the available drawing
positions. Four- and five-point modes use the curve's local tangent or a
vanishing-point direction, keeping lines and arrows straight. Perspective
directions take priority over Shift's 15° angles. Turn off **Snap to grid** or
hold **Alt** to draw freely.

Choose the separate **Perspective line** icon in the SVG toolbar to follow a
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

Keep the grid and **Snap to grid** on, plus the main canvas **Snap** switch when
drawing there. Hold **Alt**, turn snapping off, or hide the grid to draw a free
straight line. Each stroke is an open SVG path with no fill, its own stroke
color, width, dash style, opacity, and one undo step. Choose **Edit SVG** on the
main canvas to edit its path. Moving or resizing it transforms the saved
artwork; changing the grid affects new strokes. The ordinary Line and Arrow
tools continue to stay straight, using a curve's local direction when snapping.

**Perspective rectangle** is a separate tool beside Rectangle in the SVG
toolbar. Choose **Front**, **Side**, or **Ground** in its **Perspective plane**
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
choose **Edit SVG** on the main canvas to edit its paths and colors. Moving or
resizing an existing face transforms its saved artwork; changing grid settings
affects new drawings.

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

Documents store SVG source in schema 8; earlier documents migrate on load. Tool
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
