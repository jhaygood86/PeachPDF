# A block-level float is laid out in one piece, and moves whole when it fits on a page

Closes #1339 and #1340. Split out of #1334 (auto-height scroll containers), where it was needed to let a
wrapper around a float break. Builds on #1349's `CssBox.LayoutBlockChildUnbroken`.

## What was wrong

A *block-level* float (one placed by the block frame, not by the inline flow's `FlowFloatChild`) broke
between its lines at a page boundary. Its break ended the layout pass: the next pass resumed inside the
float on page 2, while the in-flow content laid out beside it was placed back on page 1, which was already
emitted. Measured on a 300×200pt page:
- a block beside a 30-line float drew only B27–B30 (#1339);
- the line after a float that moved to the next page was drawn on both pages (#1340);
- inside a fragmenting `overflow: hidden` wrapper, the text beside a float was drawn on no page (the #1334
  review's repro A).

## The load-bearing idea

A block-level float is laid out unbroken, the same way the inline flow's float already was (#1348). The
block frame calls `CssBox.LayoutBlockChild` → `LayoutBlockChildUnbroken`. Its break no longer ends the pass,
so the content beside it is laid out on the pages it belongs to.

A float that then straddles a page boundary but fits on a page is moved whole to the next page
(`CssBox.MoveWholeOntoTheNextPageIfItFits`). It is called from both the block frame and `FlowFloatChild`.
That is safe because the float is placed before the in-flow content after it. One taller than a page is
sliced ([its gap](../accepted-gaps/a-float-taller-than-a-page-is-sliced-not-fragmented.md), #317).

An earlier attempt at laying every float out unbroken broke floats that are or hold a multi-column
container, because the columns engine needs the fragmentainer that detaching removes. So those keep the
breaking path (`IsOrHoldsAMultiColumnContainer`). The float's own `columns` was missed at first:
`float: left; columns: 2` lost W18–W20 until a review caught it.

## Review findings, each reproduced on a 300×200pt page

- **A float in a column** was laid out unbroken too, and its lines past the column's foot were drawn below
  the column, outside the page band. A column does not continue a slice the way the next page does, so a
  float in a column (`CurrentFragmentainer.HasOwnBand`) keeps the breaking path.
- **A later float rose above the moved one.** Its static position was still on the page before, and
  `FloatBox` placed it there. CSS 2.1 §9.5.1 rule 5 is now enforced:
  - against earlier floats among the same containing block's children (`LowestOuterTopOfAnEarlierFloat`);
  - against floats moved from inside an earlier block in the same formatting context
    (`HtmlContainerInt.MovedFloats`, cleared per layout, compared by tree order).

  The clamp compares document Y, so it runs only outside a column: every column spans the same Y range, and
  a float low in column 1 held a later float at the top of column 2 down to its height. Two `float: right`
  boxes side by side are broken on `main` independently of this (#1374), so the test asserts only the top
  for right floats.
- **A moved `inside`/`outside` float kept the old page's side.** After the move the float is placed again at
  the new page top. `FloatPositionBesideTheFloatsAt` is the position-only core split out of
  `FloatBoxLeft`/`FloatBoxRight`, so the box is not relocated twice, and the float is translated there with
  its content.
- **A relative offset decided the move and was then dropped.** The move is decided on `StaticTop`/
  `StaticBottom` and keeps `RelativeOffsetY`.
- **The usable band.** A moved float landed on a `float: top` figure. The move now measures against each
  page's band net of the top-float strip, a footnote area and a bottom page float.
- **Fixed, running and absolute boxes with a `float` value** are not floats (CSS 2.1 §9.7). They are excluded
  from the float path and from the rule 5 scan. Placed by its offsets far down the page, such a box used to
  push the real float onto page 2.
- **A forced break inside a float is no longer honoured**, since the fragmentainer is detached. css-break-3
  §3.1 makes that optional outside the root's flow, so it is documented, not tracked.
- **Repeated layout was not stable** (the third #1334 review). The inline flow never places a plain inline
  box's `Location`, and the move's `OffsetTop` translated it anyway. So the anonymous text box inside a moved
  float gained the move's distance on every layout of the same tree (+52pt each time). `PdfGenerator`'s
  measure-then-final passes and `target-counter` documents both lay out repeatedly. The flow now resets a
  plain inline box's `Location` where it resets its rectangles (`ResumeOrdinal == 0`).
  `MovedFloatRelayoutIdempotencyTests` compares every box, anonymous ones included, and every word, over
  four layouts against a fresh one.

## Not done here

A wrapper around a float still stays monolithic, because the auto-height scroll container change (#1321)
keeps it so until this lands. Letting a float into a fragmenting wrapper is a follow-up once both are on
`main`. That is what makes the clearfix page layout (a floated menu beside a long text column) break
between its lines.

## Evidence

`FloatsAcrossPagesIntegrationTests` and `MovedFloatRelayoutIdempotencyTests`. The block beside a tall float,
the moved float, rule 5 across siblings, the usable band and the `outside` side all fail without the change,
and so does the re-layout stability test.
