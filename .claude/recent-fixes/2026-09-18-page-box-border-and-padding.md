# `@page`'s own border/padding now genuinely narrow the content area (issue #1147)

Closed the accepted gap in `page-box-border-and-padding-are-not-implemented.md` (deleted). `@page`'s
`border-*`/`padding-*` now resolve into `PageGeometryTable.PageBandGeometry` (8 new fields:
`Border{Left,Top,Right,Bottom}Pt`/`Padding{Left,Top,Right,Bottom}Pt`, plus two derived
`ContentLeftPt`/`ContentTopPt` convenience properties) and genuinely shrink `BandWidth`/`BandHeight` on
top of margin. `PdfGenerator` paints the page box's own border as a new layer (canvas < page border <
content, per css-page-3 §3.1) and branches `background-origin`/`-clip` among border-box/padding-box/
content-box.

## Load-bearing idea: reuse, don't re-derive, and resolve border/padding against the MERGED page style

`PageRuleResolver.ResolvePageBorderAndPadding` takes the **per-declaration-merged** page style
(`SelectApplicablePageStyle`), not `SelectPageRule`'s single-winner rule that `margin`/`size` still use.
Border/padding are new properties with no back-compat reason to inherit the older, less spec-accurate
selection - and this also means `PdfGenerator`'s own `applicablePageStyle` (already computed for
`background`) is EXACTLY the same value `PageGeometryTable.Compute()` resolves border/padding against
for the same (pageNumber, activeName), so the border-paint step's own `ResolveBorderWidthPt` recomputation
(needed since `MarginBoxRenderer.PaintBorder` takes raw style + emPt/remPt, not precomputed geom fields)
is guaranteed byte-identical to what layout already charged space for - both call the exact same static
helpers with the exact same inputs.

`MarginBoxRenderer.BorderExtent`/`PaddingExtent`/`Shrink`/`PaintBorder` were already fully
`CssBox`-free and generic over `StyleDeclaration` internally (just wrapped in `MarginStyleRule` for the
`MarginStyleRule`-based callers) - promoting them to also accept a bare `StyleDeclaration` directly (a
thin delegating overload) and to `internal` visibility was all that was needed to reuse them for `@page`'s
own border/padding, with zero new box-model arithmetic written.

## What was found by running it, not by reading it

- **`@page` background's positioning area was buggier than the issue's own framing suggested.** The
  accepted-gap file described `background-origin`/`-clip` as "moot" because there was no border/padding
  to distinguish - but even with only a MARGIN (no border/padding at all), the pre-fix code painted the
  background across the FULL PHYSICAL SHEET, including the page's own margin area. That's wrong
  independently of border/padding: background never extends into any box's own margin, page box
  included. Fixing `resolvePositioningRect` correctly (border-box = sheet minus margin) surfaced this as
  a **second**, previously-passing-test-enforced bug: `PageBackgroundIntegrationTests`'s
  `PageBackgroundColor_FillsWholeSheet_WhenNoCanvasBackground` and
  `PageFirstBackground_OverridesBaseRule_OnlyOnPageOne` explicitly asserted the old (buggy) full-sheet
  fill with a 20pt configured margin and had to be updated to the correct margin-inset rect. Running the
  full suite caught exactly these two failures and nothing else - strong evidence the fix is otherwise
  isolated (see below).
- **Layout-space anchoring does NOT shift with border/padding - only the band SIZE does.** A first
  attempt at the layout-integration tests asserted a paragraph's `Location.X`/`Location.Y` equal the
  resolved content-box inset (`margin + border + padding`). That's wrong: `HtmlContainerInt` keeps
  content anchored at the BASE `MarginLeft`/`MarginTop` in its own coordinate space regardless of any
  per-page `@page` geometry (this was already true for margin-only overrides, documented on
  `PageContentRightOf`) - only `PdfGenerator`'s per-page paint-time `deltaX`/`deltaY` translate maps that
  fixed anchor onto each page's own physical origin. The directly observable LAYOUT-level effect of
  border/padding is the band's SIZE (`PageGeometry.GetPage(k).BandWidth`/`BandHeight`), not an absolute
  box position - except across a page break, where the NEXT page's starting Y (`PageTopOf`) does move,
  since slot tops accumulate the previous slot's own (now-shrunk) `BandHeight`.
- **A page-margin box's own position needs literally zero code change.** css-page-3 §5.3.1 defines a
  margin box's containing-block "available width" as `border-left + padding-left + page-area-width +
  padding-right + border-right` - i.e. exactly the full margin-to-margin extent, regardless of how that
  extent subdivides into border/padding/content. `MarginBoxRenderer.MarginAreaWidth`/`MarginAreaHeight`
  already compute exactly `page.Width - mL - mR` with no border/padding parameter at all, so they were
  already correct by construction. Verified this holds by generating two PDFs (one with `@page` border+
  padding declared, one without) and diffing the `@top-center` margin box's own `Td` (text position)
  operator - byte-identical in both, while the body text's own `Td` (correctly) differs.
- **Two-renderer rasterization (PDFium + MuPDF) agreed exactly** on a `border: 6pt solid navy; padding:
  20pt; background-clip: content-box` fixture: navy border, a visible WHITE gap for the excluded padding
  band, then the light-blue content-box fill starting exactly at the text - confirming the border-box ->
  padding-box -> content-box shrink chain paints correctly, not just that the right tokens appear in the
  content stream.

## What was deliberately not done

- **No change to `background-attachment: fixed`'s own positioning area** (still the full sheet, matching
  the page-anchored convention it already had) - that property is explicitly exempted by
  `LayeredBackgroundPainter`'s own existing logic and untouched here.
- **`PageClipOverride`'s own width formula was left exactly as-is** (`(page.Width - mL) * ppp`, reaching
  all the way to the physical paper edge rather than stopping at the content-box's own right edge) -
  this was ALREADY looser than margin-tight before #1147 (a pre-existing, intentional allowance for
  `position: fixed` content to reach into the margin area), so leaving it unchanged means border/padding
  gets the same pre-existing pass-through rather than a new, narrower behavior nobody asked for here.
- **`ResolveForMaterializedPage`'s early-exit gate now also checks the two new border/padding-override
  flags**, but a base-rule-only border/padding declaration (uniform across every page, no real
  page-number dependency) still takes the "recompute and compare" path rather than a dedicated
  "definitely uniform" fast path - the recomputed candidate is provably identical to the cached slot in
  that case, so this is a wasted-but-harmless extra `Compute()` call, not a correctness gap. Not worth
  the added complexity of distinguishing "border/padding merely declared" from "border/padding varies by
  page" here.

## Evidence

- Full suite (`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`): 12351 passed, 0
  failed, 9 skipped (2 pre-existing `PageBackgroundIntegrationTests` updated to the corrected assertions,
  as above; every other test in the suite passed unchanged).
- New coverage: `PageRuleResolverTests` (8 new `ResolvePageBorderAndPadding` cases: null style, no
  declaration, uniform border+padding, border-width-with-no-style, percentage-per-axis, negative-padding
  clamp, em-relative border-width), `PageGeometryTableBorderPaddingTests` (6 cases: no-op default,
  base-rule uniform shrink, named-pseudo-rule-only shrink, vertical/horizontal degenerate fallback,
  percentage-per-axis), `PageBorderPaddingLayoutIntegrationTests` (7 cases spanning multi-page, named
  page, `:left`/`:right`, multi-column re-wrap, margin-box position isolation, and a footnote-area
  smoke test), plus 2 new + 1 fixed test in `PageBackgroundIntegrationTests`.
- Two-renderer (PDFium + MuPDF) rasterization of a real border+padding+background-clip fixture, and of
  the regenerated `paged_media_page_and_margin_box_background` showcase (now with a border+padding cover
  page) - both agree pixel-for-pixel on the border/padding/content-box bands.
