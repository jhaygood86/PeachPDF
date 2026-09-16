# `@page`/margin-box backgrounds, and margin-box border (#1082, closes #943)

`@page { background-color: ... }` already parsed (the generic declaration path every rule body uses)
and cascaded (`PageRuleResolver.SelectApplicablePageStyle`) correctly - it was simply never painted.
Same gap one level down for a margin box's own `background`, and margin box `border` had never been
painted or charged space at all (tracked as #943).

## Load-bearing idea

A `@page` box and a page-margin box both need to paint a CSS `background` with no real, laid-out
`CssBox` to read from - `FragmentPainter.PaintBackground` is too tightly coupled to a box's own
border/padding/radius/glyph-outline state to reuse directly. One new shared primitive,
`LayeredBackgroundPainter.PaintAsync`, does the color-then-layers algorithm against an arbitrary rect,
parameterized on a `resolvePositioningRect(originOrClipValue)` delegate so each caller supplies its own
box-model story: `@page` always returns the same full-sheet rect (no page-box border/padding exists to
distinguish), a margin box switches between its real border-box/padding-box/content-box rects. Follows
the same "stand-in `CssBox` for em/rem length resolution only, never for geometry" pattern
`MarginBoxRenderer.PaintImage` already established for `content: url(...)`.

Margin-box border closes #943 as a plain four-independent-edge stroke via
`BordersDrawHandler.DrawCollapsedSegment` (already used for collapsed table borders) - not the
mitred-corner machinery a real `CssBox`'s border uses. #943 itself flagged this as an open design
question; the answer is that a margin box never fragments across pages, so unlike a real box's border
it has no shared corner with a neighboring fragment to mitre into. Adjacent edges simply overlap at the
corners (both painted full-width/height), which is visually identical to a mitre for the common case
(one solid opaque color) and an accepted simplification otherwise.

`MarginBoxRenderer.BoxModelExtent` (previously margin+padding only) is now the sum of three siblings -
`MarginExtent`/`PaddingExtent`/`BorderExtent` - so `ApplyBoxModel`/`GetMarginBoxRect`'s existing callers
pick up border space-charging with no further change, exactly as #943 predicted. Two new rect helpers,
`ApplyMarginOnly` (border-box) and `ApplyMarginAndBorder` (padding-box), sit between the existing outer
slot and `ApplyBoxModel`'s content-box, giving `background-origin`/`-clip` three genuinely distinct
rects to choose from on a margin box (unlike `@page`, where they collapse to one).

## What running it turned up

- **An unset `border-*-style` must charge zero space, not "medium".** `border-*-style`'s own initial
  value is `none` (CSS 2.1 §8.5.3), so an *unset* style computes to `none` exactly like an explicit one.
  The first version only zeroed width for an explicit `none`/`hidden`, so a stray `border-top-width`
  with no `border-top-style` charged 1.5pt of space no author asked for - caught by seven existing
  `MarginBoxRendererBoxModelTests`/`MarginBoxRendererSizingTests` fixtures failing by exactly that
  amount once border joined `BoxModelExtent`'s sum.
- **`thin`/`medium`/`thick` are already resolved before this file ever sees them.** `border-*-width`'s
  CSS-OM property resolves those keywords to their UA-default pixel lengths (`1px`/`3px`/`5px`) at
  declaration-set time - confirmed empirically, not assumed - so `ResolveBorderWidthPt`'s own
  keyword-literal branch is a defensive fallback, not the path a real declaration takes. Matters for
  matching PDF output against what CSS actually specifies (the real UA defaults), not an invented
  convention.
- **A margin box's declared `width` is the content-box dimension, so its border-box is *wider* than the
  declared number**, not equal to it - `border: 6pt ... ; border-right: 2pt ...` on a `width: 200pt` box
  makes the border-box 208pt, not 200pt. Cost a round of wrong expected coordinates in the border
  content-stream tests before re-deriving the geometry from `GetMarginBoxRect`'s own outer-allocation
  rule (css-page-3 §5.3.2) instead of assuming the declared width was the paint rect.
- **PDF Y is flipped from this library's document-space Y** in the raw content stream a margin box
  paints into - a `border-top` edge's stroke ends up at the *larger* PDF-Y end of the box's own range,
  a `border-bottom` edge's at the smaller end. Straightforward once observed via an actual generated
  PDF, easy to get backwards from first principles.

## Deliberately not done

`@page` box's own `border`/`padding` (as opposed to a margin box's) - out of scope for #1082, which
only asked for background there; the page box has no border/padding-driven geometry to paint into.
Recorded as its own accepted gap
([page-box-border-and-padding-are-not-implemented.md](../accepted-gaps/page-box-border-and-padding-are-not-implemented.md))
and tracked as [#1147](https://github.com/jhaygood86/PeachPDF/issues/1147), since it's a genuine
spec deviation (css-page-3 §3 gives the page box a real border/padding model) rather than just an
unimplemented convenience.

## Evidence

New/extended: `PageRuleResolverTests`/`PdfGeneratorSelectPageRuleTests` (background cascade merge),
`PageBackgroundIntegrationTests`, `MarginBoxRendererBackgroundTests`, `MarginBoxRendererBorderTests`,
`MarginBoxRendererBoxModelTests` (border space-charging + the border-box/padding-box/content-box split).
Full suite green on net8.0 (12,162 tests), 0 build warnings (`-t:Rebuild`, whole solution).
