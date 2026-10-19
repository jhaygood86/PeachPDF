# `PeachBrackets` gets a public API: `HtmlDocument`

Phase 3b, step two: the extracted engine is usable on its own. `HtmlDocument.LoadAsync(html, renderContext, options)` parses and cascades, `LayoutAsync(measuringCanvas)` paginates,
`PaintPageAsync(index, canvas)` draws one page onto any `Canvas`. `Annotations` (links, outline, form controls), `StructureTagSink` (tagged-output seam), `ClippedText`, `Language` and `Metadata`
carry out what the pages hold besides pixels. The guide is `docs/peachbrackets.md`; the reviewed snapshot is `src/PeachBrackets/PublicApi.txt`.

- **The load-bearing move was extracting the page loop, not designing types.** Painting a page was ~230 lines inside `PdfGenerator.RenderPagesCore` (the `@page` background and border, canvas
  background, content, footnote areas, margin boxes). It now lives in `Html/Pages/PagePainter` and asks its caller for two things only: a canvas per stage (`CanvasLease`: a PDF page hands out a
  fresh canvas per stage and disposes it; a bitmap reuses one and releases nothing) and a way to shift the origin (a PDF page's `Shift`; a canvas's `PushTransform`). `PdfGenerator` and
  `HtmlDocument.PaintPageAsync` both go through it, so the two cannot drift. The whole `PeachPDF.Tests` suite (17444) passed unchanged across the extraction, which is the proof it is behavior-neutral.
- **What became public was already shaped for it.** The annotation model (`PageLink`, `Bookmark`, `PageFormField`, `DocumentAnnotations`) and `IStructureTagSink`/`StructureNode` were written as
  format-neutral seams in phase 1; this made them public with docs, moved the form-field value types into their own file, and made the constructors and factories that only the engine calls `internal`.
  `StructureNode`'s implicit conversion from `CssBox` could not stay (a public type cannot expose an internal parameter in an operator), so call sites use `StructureNode.Of(box)`.
- **Traps found by running it.** After `@page { margin }` the margins live on the container, not in the options, so the content width must be computed from the container's corrected margins (using the
  options' first gave a 280pt sheet for a 300pt `@page`). A public member's documentation must not `cref` an internal type: it renders as a link to learn.microsoft.com for a type that is not Microsoft's,
  and the API-reference link check fails (the PDF writer hit the same). `PeachBrackets.Tests` samples pixels at known positions; sampling inside the margin gave a false failure for an animation test.
- **Not done:** PeachPDF still drives `HtmlContainerInt` through internals (`InternalsVisibleTo` stays as the temporary bridge) for the declarative document API, ShrinkToFit rescaling, and the
  layout passes it repeats; moving those onto `HtmlDocument` is the next step, and then the grant goes.
- **Evidence:** `PeachBrackets.Tests` 22 (public API only, painted onto `PeachDrawing`'s bitmap canvas), `PeachPDF.Tests` 17444, `PeachDrawing.PDF.Tests` 959, Cli 213, source-generator 122; rebuild is
  warning-free; the generated API reference for the package has no link to an internal type.
