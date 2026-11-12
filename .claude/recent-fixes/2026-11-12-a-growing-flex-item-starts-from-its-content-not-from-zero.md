# A growing flex item with an `auto` basis starts from its content, not from 0

**Symptom.** `flex: 1 1 auto` (or `flex-grow: 1` alone) items came out equal whatever they held: `MeasureItem` gave any item with
`flex-grow` above 0 and an auto basis a base size of 0 (`hypothetical = 0`, "so all free space is distributed via growth"), which is
right for `flex: 1` (`1 1 0%`, a basis of its own, never reaches that branch) but not for `auto`, which css-flexbox-1 §9.2 sizes from the
content. Measured against headless Chrome on a 500px row: `Un1mmm…` (one 349px word) beside `y1 y2` was 349/151 here and 405.5/94.5 in
Chrome; shorter text was 250/250 against 298.9/201.1. Found by a maintainer's review of the multi-column intrinsic-width change, where the
same shape holding a `column-width` container went from 329px (by accident: the unbreakable word's automatic minimum floored the equal split)
to 250px; the multi-column sizing was right (Chrome's min-content for that container is 100px) and the flex rule was what was off.

**Fix.** The two places that derive an item's base size (`MeasureItem`, `RederiveItem` for an item already laid out on an earlier page) no
longer special-case `flex-grow`: an auto-basis item is measured from its content like any other. That alone moves the free space, so four
pieces of the algorithm had to follow, each found by sweeping random rows against Chrome (below), not by reading the spec:

- **`min-width` does not raise a growing item's base.** The base stays the content size; the minimum is applied to the size the item ends
  up with (`ClampMainAxis`). `400pt` row, `flex-grow: 1` items of 100pt and 20pt content, the second with `min-width: 120pt`: Chrome 240/160
  (free space is 400 - 120), not 190/210.
- **A growing line resolves `max-width` and `min-width` violations** (§9.7 step 4). `GrowAgainstAutoMinimums` now clamps to a maximum as
  well as a minimum and freezes by the sign of the total violation (min violators when positive, max violators when negative), so an item held
  to its `max-width` takes no more and the rest share what it cannot use. A growing item whose content is wider than its `max-width` has the
  `max-width` as its hypothetical size, which is what keeps a line with 1000pt of content in one item and a 100pt maximum on the growing side
  rather than shrinking. Before, `ClampMainAxis` only cut the result at the end and the cut space was lost (`flex: 2 1 0%` beside a
  `max-width: 150px` item was left at half of Chrome's width).
- **A line is sent through that loop** when an item has a `max-width`, or an explicit `min-width` above its base size, as well as for an
  automatic minimum (the plain arithmetic is kept otherwise). An explicit `min-width` violation used to be redistributed only because every
  grower's base of 0 was below its automatic minimum, which is what ran the loop.
- **`ClampMainAxis` and the `min-width` raise follow `box-sizing`.** They added the item's padding and border to a `max-width`/`min-width` even
  on a `box-sizing: border-box` item, so the growth loop (which now takes its maximum from it) let a border-box item with `padding: 20pt;
  max-width: 100pt` grow to 140pt (Chrome 100pt). Both now add `MainBoxSizeIncluded`, which is 0 for border-box. Found by the review; it also
  moves the `charts_css` showcase's bars (`box-sizing: border-box`, padding and `min-height`) from 26pt to 20.1pt tall, which is Chrome's
  number, measured on the same page.
- **The overflowing-line cap on an automatic minimum is gone** (`RecordAutoMinimum`), and a `flex-shrink: 0` item that grows is no longer
  skipped. Both only mattered once growers could overflow a line: a `flex: 1 1 0%` item beside content that fills the line was capped at its
  base of 0 and painted as nothing, where Chrome raises it to its own content (an `x y` item was 0 here, 8px in Chrome).

**Evidence.** Random rows against headless Chrome (2-4 items; row and column; `flex` shorthands with grow 0-3, shrink 0/1, basis
`auto`/`0%`/`40px`; padding, `min-width`, `max-width`; gaps; unbreakable and breakable text), main-axis size within 1px: seed 7, 60 cases,
22 match on `main` and 59 with the change; seed 23, 140 cases, 56 and 133. No case that matched on `main` stops matching. The seven that
still differ were not looked into. All 250 showcases paint identical content to `main` except `charts_css` (above), `flexbox` (the
`grow:1,2,3` row, now 69.5/119/168.7pt against Chrome's 69.25/119.2/168.75 where it was an exact 1:2:3 split) and the two ZUGFeRD invoices,
whose three `Grow()` columns are now sized by their content (see the migration note).

**Found while measuring, not fixed here (both are the same on `main`; #2356 and #2357).** `flex-grow` with a sum below 1 hands out all the free space where §9.7
step 4 hands out that fraction of it (`flex-grow: .3` alone in a 400pt row is 400pt, Chrome 190pt for a 100pt item). A column item with a
`max-height` below its content (`flex-grow: 1; max-height: 100pt` over 1000pt of content) paints 1000pt tall where Chrome paints 100pt, though
the item beside it now gets the right size.

**Not done.** The corpora the maintainer ran (repo HTML/MHTML files, flex/float/table/random corpora) were not available here, so
documents outside the showcases are unchecked. `min-width`/`max-width` violation redistribution for a shrinking line is untouched.
