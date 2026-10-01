# A line that no float leaves room on moves below the float, and a right float no longer pushes the cursor (#1531)

Two floats that leave no room between them (a 131pt left float and a 129pt right float in 260pt) lost the first two
words of the paragraph below them: both were placed at X=280, the page's content edge, and clipped.

## Two causes, found by tracing the first word

- **A right float was reported as a left float.** The line cursor's "where does a left float push me" lookup
  (`DomUtils.GetLastLeftIntersectingFloatBox`) goes through `IsFloatIntersecting`'s `Floating.Left` arm, which never
  checks the target's side. With the cursor at 151 (the left float's right edge) and a right float whose left edge was
  also 151, the right float counted as an obstruction and the cursor was pushed across it to 280. Fixed with
  `CssFloatCoordinates.LeftFloatsOnly`, set by that one caller; `FloatBoxLeft` still needs right floats from the same
  arm, so the arm itself is unchanged for it. A right float is bounded by `RightFloatAt`'s lookahead instead.
- **An empty line was never shifted below the floats that crowd it.** The wrap decision only wraps off a line that
  holds something, so a first word with no room beside the floats was placed in whatever room remained. CSS 2.1 §9.5:
  "If a shortened line box is too small to contain any content, then the line box is shifted downward ... until either
  some content fits or there are no more floats present." `ShiftEmptyLineBelowCrowdingFloats` does that, from the
  line's own start, moving to the bottom margin edge of whichever blocking float ends first and asking again.

## Traps

- The line records its top in `CssLineBox.FlowTop` as a **minimum** (`CommitLineExtent`), set by the speculative
  `GrowLineToItsExtent` before the shift. Moving only `CurrentY` left every word at the old Y (verified: y stayed 20.3
  while the shift loop ran). `FlowTop` is cleared before the line is grown again at the new Y.
- The cursor arrives already pushed past a left float, and the point-collision lookup stops reporting a float once the
  cursor is beyond it, so the helper asks from the line's start and re-applies the same inset past the float.
- A recording-only check ("every word was drawn") passed on `main` for this document. The words were painted but placed
  past the page's right edge, which the PDF clips away. `PaintedWords.LayOutAndCollectVisibleAsync` counts a word only if
  its rectangle is inside the page band.

## A hang the unit tests did not see

The first version of the shift looped forever on a generated document (a 150pt spacer, then `columns: 3` holding a
paragraph, a 75pt x 129pt `float: right` and text). The float is in one column but still narrows the lines of the
others as far as the line lookups go, so each column shifted its first line below it, broke away, and the next column
did the same. Two guards: the shift is not taken inside a column (`HasOwnBand`), and not when the target Y is at or
past the page band being filled (the original state is restored, so the line keeps the room that is left). Found only
by running 300 generated documents: the suite passed with the hang in place.

## Not done

The 139pt row of the issue (floats that do not fit side by side) is the right float dropping below the left one, which
#1522 fixed; the first line then sits beside the left float at the top, and the test pins that.

Evidence: `FloatPlacementTests` rows for 120, 129 and 139pt (words and first-line position); full net8.0 suite, 14836
tests, passes. No generated corpus was run, so the earlier worry in the border-box accepted gap (a first-word shift
dropping words in some documents) is untested here.
