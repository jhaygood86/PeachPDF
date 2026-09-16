# A dotted/dashed outline is fitted to the ring's outer edge, not its centreline

`OutlineDrawHandler.DrawDottedOrDashedLine` spanned each side's stroke from
`rect.Left - mid` to `rect.Right + mid`, where `mid = outline-offset + outline-width / 2` — the point
at which the band's *centreline* crosses the corner's mitre diagonal. That is the wrong length to fit
a pattern to, and it is off by exactly one whole outline-width per side.

## The load-bearing idea

Chrome does not have a separate outline painter for the non-`auto` styles. `PaintSingleRectOutline`
hands its **border** painter an outer rectangle inflated by `outline-offset + outline-width`, with
uniform per-side widths, and the ordinary border path draws it. So the correct model is a pure
restatement: *an outline is a border on the inflated rectangle*. `BordersDrawHandler`'s own
dotted/dashed edge already spans `rect.Left..rect.Right` — its outer edge, corner squares included —
so the outline's must span `rect.Left - (offset + width) .. rect.Right + (offset + width)`.

The `mid` value stays, but only for the across-axis coordinate (the stroke does sit on the band's
centreline). Only the along-axis span changed.

## What this produced before

`StyledStrokeFitting.Fit` measures the span it is handed, so a span one width short both

- picks a different dash count and a differently-sized gap — for a 120pt box with a 6pt dotted
  outline, period 12 over a 126pt span instead of period 11.455 over a 131.73pt span; and
- pulls the first and last mark half a width in from the corner. For `dotted` that put the top edge's
  first dot and the left edge's first dot half a width apart diagonally, rendering the corner as a
  figure-8 blob instead of one dot; for `dashed` it cut a notch out of the corner L.

Both were confirmed by rasterizing the same page before and after through PDFium and MuPDF — the two
renderers agree, and the "after" corners are pixel-identical in structure to the equivalent border's.

## The test that actually pins this

`OutlineStyleDottedOrDashed_MatchesTheEquivalentBorder` renders `outline-offset: -outline-width`,
which lands the ring exactly where an equal-width border sits, and asserts the two produce the same
four `DrawLine` calls including the same dash arrays. Border's dotted/dashed rendering was measured
against Chrome in PR #1126, so this makes border the oracle for outline rather than re-deriving
Chrome's numbers a second time. It fails on the pre-fix code for both styles.

`OutlineStyleDottedOrDashed_LinesReachTheRingsOuterCorner_NoGap` asserted the *old* `mid` span as if
it were the invariant, which is why the defect survived #1126 — it was renamed to
`..._LinesSpanTheRingsOuterEdge` and now asserts the outer-edge span. Worth knowing: a test written
from the implementation's own geometry rather than from the reference renderer's will ratify whatever
the implementation does.

## Deliberately not done

The corner join still is not mitred — adjacent sides simply overlap in the corner square, the same
simplification `BordersDrawHandler`'s square dotted/dashed branch accepts. That is unchanged by this
fix and is what makes the corner dot/L read correctly once the spans are right.
