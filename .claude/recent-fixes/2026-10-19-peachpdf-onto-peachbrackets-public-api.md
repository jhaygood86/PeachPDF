# PeachPDF moved onto PeachBrackets' public API

PeachPDF and `PeachPDF.Cli` no longer have `InternalsVisibleTo` access to PeachBrackets; only `PeachPDF.Tests` keeps a grant (a temporary
bridge, see below). `PeachPDF/PublicApi.txt` is unchanged.

**What moved.**
- The declarative builders (`Layout/*`) fill a `DeclarativePage` through `PeachBrackets.Building` (`DocumentBuildContext`, `DocumentNode`,
  `StyleSheet`, `SvgImage`) instead of creating `CssBox`es. `PageDescriptorBuilder.Page` replaces `RootBox`/`PageRules`; the geometry the
  builder fixed explicitly travels as `PageGeometryOverrides`, computed from the set values rather than flags kept beside them.
- `HtmlContainer` (PeachPDF's wrapper around `HtmlContainerInt`) is gone. `PdfGenerator` loads with `HtmlDocument.LoadAsync`/`CreateAsync`,
  lays out with `LayoutAsync`, and paints each page with `PaintPageAsync`; `SetContent` and the `PdfGenerator.Select*` shims over
  `PagePainter` were deleted (tests call `PagePainter` directly).
- `PdfSvgGlyphPainter` and `GlyphDocumentKey` moved into PeachBrackets as the public `Svg.SvgGlyphPainter`; the font defaults, the
  `font-variation-settings` decoding and `data:` URI decoding a `RenderContext` needs are the public `FontDefaults` and `DataUri`.

**Trap.** `ShrinkToFit`/`ScaleToPageSize` reload the document after the measuring layout (the page's resolved size depends on the layout scale,
which the font cache is keyed on), so `AddPdfPages` disposes and re-`LoadAsync`es and must re-read `ContentArea` and recreate the measuring canvas
from it; reusing the first canvas lays the second document out against the wrong band.

**Tests.** `PeachPDF.Tests` still asserts on the box tree (computed styles and positions after layout), which needs the internals. They reach it
through `DocumentNode.Box` / `HtmlDocument.Container` and the `DeclarativeHarness` and `PdfGeneratorLayoutHarness` helpers in `TestSupport`.
Moving those into `PeachBrackets.Tests` (with a headless `RenderContext`, since they build `PdfRenderContext` today) is what removes the last grant.
