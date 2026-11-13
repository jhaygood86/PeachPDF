# A bidi-split inline box keeps its edges beside its own words and is drawn as separate pieces

Found by the WPT runner (`css/CSS2/bidi-text/bidi-001.xht`, now passing). One `<span>` with border and padding contains `AB` and an RLO
that its PDF closes *outside* the span: logically `AB`(level 0, span) `CD`(1, span) `EF`(1, body) `GH`(0, body). Visually `EF` lies
between `AB` and `CD`. Two defects:

## Cause B - edge spacing reflected into the gap (`ApplyBidiReordering`)
The flow reserves a box's physical left/right border+padding+margin in logical order, between words. The run reflection kept every
gap where it was, so the span's right edge (after `CD`) ended up *before* `CD`, inside the run. Now each word is reflected together with the
room reserved just before it (boxes that open at it) and just after it (boxes that close at it): `MeasureInlineEdgeSpacing` derives
`lead[]`/`trail[]` per word from the box chain, counting a box only on the line that opens or closes it (or every line under
`box-decoration-break: clone`) and capping each by the real gap so a mis-measure can only under-reserve, never overlap. Lines that
need no reordering still return early; LTR runs reflect to the same positions as before (lead and trail cancel in the offsets).

## Cause A - one rectangle per box per line (`BubbleRectangles`)
A box's rectangle on a line is the union of its words, so the span's rectangle ran from `AB` to `CD` and swallowed `EF`. The union stays
(line height, hit testing, relative offsets and the emitter's word membership all read it); `ComputeInlineSplitPieces` additionally records
`CssLineBox.SplitPieces[box]`: the visually contiguous pieces, **relative to the union's left edge** (so any later move of the rectangle
keeps them valid, which avoided touching the ~10 places that rewrite `Rectangles`), each with `HasLeftEdge`/`HasRightEdge` (the box itself
opens/closes there). It runs last in `FinalizeLineBoxes`, only on lines with a nonzero bidi level or an rtl owner. `FragmentEmitter.SplitAcrossPieces`
then replaces that line's one `LineFragment` by one per piece (same `Line`), ANDing the edge flags into the slice; paint already draws N
fragments with per-fragment edge flags, so it did not change.

## Found by running it
- The first fix (B alone) cut the failing pixels 1,617 -> 512 and moved the text right, but the border was still one box: A is a separate,
  visible defect, and the reftest only passes with both.
- Full WPT run before/after: 1 gained (bidi-001), 0 lost; Engine.Tests 19,816, PeachPDF.Tests 1,978, PeachBrackets.Tests 1,208 green.

## Not done
- Vertical writing modes have their own reordering (`ApplyVerticalBidiReordering`) and are not touched.
- `box-decoration-break: clone` is counted as "edges on every line" in the pieces as `UpdateRectangle` does; no test of a split clone box.
- A box whose piece boundary is a *nested* box's words is split on any intervening word that is not the box's own; a nested box that itself
  spans the split is still recorded separately (its own pieces), which looks right in the one case checked (bidi-001) but is not exhaustively tested.
