# `display: block` images/SVGs no longer get a phantom line-height strut

A table cell holding only a `display: block` `<img>` with an explicit width/height laid out ~1.6-2.4pt
taller than Chrome's equivalent row, and the excess was per row: a table of such rows drifted steadily
down the page, enough on a real document to push a trailing block onto a second page Chrome doesn't
need. Bisected by the reporter to e4b5d62d ("Baseline-align atomic inlines...", #1103); before that
commit each row was ~0.75pt *short* of Chrome, after it ~1.6-2.4pt *too tall* - the same underlying
mechanism, sign flipped.

## The load-bearing idea

`DomParser.CorrectReplacedElementBoxes` already has a deliberate hack predating this fix: this engine
can only size a replaced element as a single atomic inline "word", so a `display: block`
`<img>`/`<svg>` gets wrapped in a brand-new anonymous block and has its own `Display` forced back to
`inline`. By the time `CssLayoutEngine` runs, nothing distinguished that synthetic wrapper's line from
a real author-written inline formatting context - so `LineBoxContributionOf` kept unconditionally
reserving CSS 2.1 §10.8's strut (the containing block's own font/line-height) under it. That is exactly
the gap `display: block` exists to opt an image out of; here it was silently opted back in, once per
element.

A new `CssBox.IsReplacedBlockWrapper` flag, set only on that synthetic wrapper, lets
`LineBoxContributionOf` skip the strut specifically there (`default` extent instead of
`HalfLeadingExtentOf(...)`) while leaving every other strut/baseline computation - including an
author-declared inline image sitting beside real text - untouched. `GrowLineToItsExtent`'s existing
`word.IsImage` branch already unions the image's own margin box on top, so with the strut zeroed the
wrapper's line becomes exactly that margin box, matching a true block box's auto height.

An earlier idea - guarding `AtomicInlineBaselineOf` on the image's own computed `display` - turned out
to be a no-op: `CorrectReplacedElementBoxes` has already overwritten the image's `Display` to `inline`
by the time that code runs, so there is no signal left there to distinguish on. The vertical-writing-mode
line engine (`CreateVerticalLineBoxes`) already special-cased `word.IsImage` to skip the strut; the
horizontal path (`FlowBox`/`GrowLineToItsExtent`/`LineBoxContributionOf`) was the one place still
missing it, and only for this synthetic-wrapper case.

## Evidence

- Rendered the issue's own repro HTML through the CLI and read word y-positions back with PyMuPDF: the
  four rows' `y0` came back `23.38, 48.13, 72.88, 112.63` and the marker paragraph after the table
  `134.24` - an exact match, to two decimal places, for the issue's own `2141ab14` (pre-regression)
  column. The compounding drift e4b5d62d introduced is gone; the residual few points short of Chrome
  in that column predate e4b5d62d and are a separate, unrelated gap the issue itself flagged as
  out of scope here.
- New `ReplacedBlockWrapperLayoutIntegrationTests`: an isolated large-font/line-height fixture asserting
  the wrapped image's containing block measures exactly the image's margin box; a reduced three-row
  table asserting no gap/overlap forms between rows and the total table height is the exact sum of the
  three image heights; a non-regression check that an ordinary (non-`display:block`) inline image beside
  text still receives the full line-height strut. Confirmed non-vacuous by reverting the
  `DomParser` one-line change and re-running: the isolated test and the table test both fail (table row
  measured 27.82pt instead of 25pt), the non-regression test still passes.
- Full net8.0 suite: 12,069 passed, 0 failed, 9 platform skips.
- Full solution rebuild: 0 warnings, 0 errors.
- Diff coverage: 98%.
