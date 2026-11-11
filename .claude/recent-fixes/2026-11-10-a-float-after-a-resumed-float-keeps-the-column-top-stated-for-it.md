# A float after the float a column fill resumed onto starts at the column's top (#2317)

Second half of #2317, after [the cross-column lookup fix](2026-11-10-a-float-of-another-column-does-not-start-a-line-at-its-far-edge.md).

**Symptom.** A tall `float: right` that continues over three columns, then a `float: left`: the left float's first lines were drawn 8pt above the page content (clipped) and over the right float's continuation, so up to eleven words were lost.

**Cause.** The #2319 fix has the fill that resumed onto a float state a column-top target for the floats after it (`_resumeTopOverride`, set in `LayoutBlockChildren`). The box then goes through `BeginLayoutPass`, which clears `_resumeTopOverride` the first time the box is reached in a layout generation. A box already reached in the generation kept the target, so the #2319 documents passed; a float reached for the first time in this fill had it wiped, and was placed from its previous in-flow sibling instead (418.9, a slot back; the column band started at 440).

**Found by running it.** Logging the fill's children (`child#4 div(FLeft) loc=(156,418.9) ... slot=2 band=(440..650)`), the override (`SET override i=4 -> 440`) and the offset the commit used (`offset.Top=418.94`) side by side: the target was set and not read.

**Change.** `_resumeTopOverrideStatedIn` records the generation a parent stated the target in; `BeginLayoutPass` keeps a target stated in the current generation when it resets a box on its first pass, and still drops one left from an earlier generation.

**Evidence.** The #2317 document draws every word once. A sweep of 128 generated documents (0 to 3 leading paragraphs, an optional right float, a left float of four sizes in one or two paragraphs, `columns: 2`) lost words in 46 before the two fixes and none after; it is kept as `TwoFloatsAfterParagraphsInColumns_EveryDocumentDrawsEveryWordOnce`. Release engine suite 19,384 (4 skipped: #2318 x3, #2320), `PeachPDF.Tests` 1,807, zero warnings.

**Not done.** #2318 (nested multi-column after a float that continues) and #2320 case 1 (a word drawn twice) are untouched and still skipped.
