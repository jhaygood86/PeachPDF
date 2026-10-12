# A column no longer measures, and forgets, every page before it

Follows [the multi-column emitter fixes](2026-10-12-a-column-container-skips-children-past-the-page.md). Their profile ended with the
emitter flat; a CPU trace of a 7,200-paragraph two-column document then showed the time had moved into the columns engine itself,
in two places, both of which re-did work for the whole document once per column.

## `MaxBottomOf`: how far down this column reaches

`CssLayoutEngineColumns.FillColumns` asked `MaxBottomOf(children, placedBelow, beyond)` after every column, which called
`DeepestBottomOf` - a recursive walk of the whole subtree - on **every child from the start of the container** up to the break.
43% of that trace (17.6s of 40s) was there. The value only matters where it exceeds `boxTop`: `columnBottom` feeds
`max(boxTop + target, columnBottom)` and `max(contentBottom, columnBottom)`, and `contentBottom` starts at `boxTop`.

A child that is a block, places itself, and ended more than **one page band** above this column's `boxTop` cannot reach it (it
would have to overflow by more than a page), so it is skipped on an O(1) test of its `ActualBottom`. With no real page grid
nothing is skipped. Everything within a band of the column, and every child that is not a self-placing block, is walked exactly
as before.

**Not done, because it was wrong:** scoping the walk to "the children this page's columns touch". A temporary comparison of
the scoped and the full result over the whole test suite found a case where a child *before* the page's first column reaches
below `boxTop` (child 0 at 289 against a `boxTop` of 230), so earlier children are not always irrelevant within a page. The
band rule above passed the same comparison with zero divergences over all 16,430 tests and every document.

## `BeyondThisColumn`: the boxes this column does not hold

`AddBoxesBeyond` put **every child from the resume index to the end** of the container (and of each box on the break chain)
into a `HashSet<CssBox>`, for every column, twice (once for the measurement, once for the capture). Then
`ForgetInCapturedInstancesFrom` copied that set into a list and called `Forget` - a recursive removal of the box and its whole
subtree - for each of them, in every fragmentainer of the page. The set is the rest of the document, and each of the three
things done with it was O(rest of the document).

`BoxesBeyondColumn` keeps what is really there: a few explicit boxes (floats, table cells) and the **ranges** `(child list, from
index)` of the chain. `Contains` looks the box up in a cached child-index map of the list (rebuilt when the list changes size or
ends; a hit is checked in place; a box listed twice counts at its last occurrence, which is the question "is any occurrence at or
after the resume point"). The questions asked of it are only about the few boxes a column holds. `BoxGeometrySnapshot` gets
`ForgetMatching`: instead of visiting every moved box, ask each box the snapshot holds whether it or an ancestor is moved - the
same removals `Forget` made one moved box at a time.

`BoxGeometrySnapshot.Capture` now takes the small `IBoxExclusion` interface instead of an `IReadOnlySet<CssBox>`.

## What was found by running it

- Wall time is noisy on this machine (other work was running; one document measured 15.9s and then 23.6s a few minutes apart), so
  the comparisons use the minimum of three runs.
- The two changes were validated by running both versions side by side and comparing, not by reading: the scoped
  `MaxBottomOf` against the full one (clamped at `boxTop`) and `ForgetMatching` against a simulation of the old recursion,
  each over every document and the whole suite. Zero divergences for the final forms; the comparison for the *first* form of
  `MaxBottomOf` is what caught the case above.
- After both, the 7,200-paragraph profile is flat: ~5.9s layout, ~3.2s parse and cascade, no frame above a few percent.

## Evidence

- Output identical to the build without it: same words and drawing operations on every page for 22 documents (prose, table,
  invoices, two-column and its variants - 3 columns, `orphans/widows: 3`, `break-inside: avoid`, `column-fill: auto`,
  `column-rule`, `column-span: all` - math, images, small documents).
- Release CLI, two-column prose, minimum of three: 4,800 paragraphs 13.7s -> 9.3s, 7,200 27.6s -> 12.4s, 12,000 33.6s (after the
  first of the two changes) -> 23.1s: linear, 2.5x the content for 2.5x the time. The 1,272-page document: 57.3s -> 23.6s
  (JIT CLI), 57.8s -> 19.2s warm in-process; the 127-page one 3.9s -> 3.7s CLI, 0.92s -> 0.81s warm.
- `BoxesBeyondColumnTests` (7: none, range, past-the-end, explicit, combination with `Enumerate` equal to `Contains` over every
  box, a list that changes, a box listed twice), three `ForgetMatching` tests against `Forget`, and `ColumnBottomMeasureTests`
  (children measured over 150 -> 600 paragraphs: 3,945 -> 55,188, 14x, without the band rule; linear with it).
- Full suite green on net8.0 in Release (16,441); whole solution `-t:Rebuild` has 0 warnings. With
  `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` the failing set is the 9 tests that already fail on `main` plus the two box-visit-count
  tests the oracle's unpruned walk inflates.
