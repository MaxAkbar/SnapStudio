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
Hold **Shift** while drawing a line or arrow, moving objects, or rotating to snap
the angle in **15° increments**. Shift still makes rectangles square and ellipses
circular, and preserves proportions when resizing.

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
