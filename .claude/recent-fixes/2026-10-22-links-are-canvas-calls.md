# Links are `Canvas` calls (`AddLink`, `AddDestinationLink`, `AddDestination`)

A page's hyperlinks used to reach the PDF only through PeachPDF's `HandleLinks`, which read `HtmlDocument.Annotations.Links` and called
`PdfComposerPage.AddLink` - so no other canvas (SVG, a third party's) could get an HTML document's links. They are now three virtual
`Canvas` members (default: nothing, return null), `HtmlDocument.PaintLinks(pageIndex, canvas)` makes the calls, and `PdfCanvas` and
`SvgCanvas` implement them. `PdfGenerator.RenderPagesCore` calls `PaintLinks` for each page after the page loop.

## What carried the design

- **`PaintLinks` is a separate call, after every page is painted - not part of `PaintPageAsync`.** `DocumentAnnotationCollector` places a
  link under a transform from `container.Placements`, which only painting a page records, and an anchor can lead to a later page. Collecting
  inside `PaintPageAsync` would cache `Annotations` after the first page and miss the rest. A destination is named when the call is made for the
  page *it is on* (`AddDestination` on that page's canvas); the link on another page only refers to the name.
- **Units follow the `Canvas` convention.** Bounds, quad and destination height are layout units (rectangles on a canvas are), so
  `PaintLinks` multiplies the points the annotation model holds by `LayoutUnitsPerPoint` and `PdfCanvas` divides them back.
- **The handle for tagged PDF.** `AddLink` returns `object?`: the PDF canvas hands back the `PdfLink`, and `HtmlDocument` passes it with the
  link's `StructureNode` to the new default member `IStructureTagSink.LinkAdded`, which `StructureTagBuilder` turns into
  `LinkAnnotationToStructureElement`. Same order as before (after `structureTagBuilder.Finish()`), so the existing tagged-PDF suites pass untouched.
- **Page index on a canvas.** `PdfComposerPage.CreateCanvas` sets `PdfCanvas.Page`; a tile or measuring canvas has none and returns null.
- A side effect worth knowing: the old code indexed `composer.Pages[link.PageIndex]` directly, which is the wrong page for the second call
  of `AddPdfPages` on one document; `RenderPagesCore` now offsets by the pages already there.

## SVG

Links and destinations are collected on the canvas and written after everything else, outside every group, so they lie on top and are in page
coordinates whatever was pushed: `<a href><rect fill="none" pointer-events="all"/></a>` (a `<polygon>` for a quad - the corners come
top-left, top-right, bottom-left, bottom-right, a polygon goes round), and `<rect id="anchor-NAME" y=TOP width=0 height=0/>` for a
destination (prefixed so a document's anchor name cannot collide with the ids the canvas writes). A link to a place on another page is a
dead fragment: each page is its own SVG.

## Evidence

`PeachPDF.Tests` (link destinations, tagged-PDF structure tree, PDF/A, file layers), `PeachDrawing.PDF.Tests`, `PeachBrackets.Engine.Tests`
all pass unchanged; new `PdfCanvasLinkTests` (annotation rectangle in points at two units to the point, named destination across pages,
quad points, page-less canvases) and `SvgLinkTests` (structure, quad order, escaping, tile/finished page, and an HTML document's external and
in-page links with the heading's height).
