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

**Tests.** The tests that assert on the box tree (computed styles and positions after layout, the cascade, painting through a spy `Canvas`) moved to
the new `PeachBrackets.Engine.Tests`, the one project granted `InternalsVisibleTo` by PeachBrackets (and by PeachPDF, for `HtmlRenderContext` and the
declarative builders). The move was driven by the compiler: remove the grant from `PeachPDF.Tests`, move every file that stops compiling, rebuild,
repeat - the compiler reports only the first failing phase's errors, so it takes several rounds, and a helper that moves (`PaintedWords`,
`PdfGeneratorLayoutHarness`) pulls its callers along. Files keep their `PeachPDF.Tests` namespace and the project sets `RootNamespace` to match, because an
embedded resource's name (`CSS/bootstrap.css`) is built from it. Helpers both projects use are linked from `PeachPDF.Tests/TestSupport`.
