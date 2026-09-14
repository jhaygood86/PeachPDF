# A definite-height decoration does not grow to overflowing content

Acid2's `strong` was already the same 6em width as Chrome. The visible mouth difference came from its
ancestor instead: `.smile div div span` declares `height: 1em`, but contains a 2em-tall floating
descendant. Layout correctly kept the span at 1em; fragment materialization then grew its background
and border rectangle back to the child's full height. That repainted the wider upper mouth bar across
the lower row, making the `strong` look wider than it was.

## The load-bearing distinction

CSS 2.1 §10.6.3 makes a definite height the used height even when in-flow content overflows it.
`FragmentEmitter.BoundsEndAtItsContent` nevertheless has one valid page-grid use: a flex/grid item
whose final commit pass pinned its dimensions before content that later fragmented beyond those
bounds was known (issue #569).

`CssBox.ItemContentSizeEverPinned` already records exactly that case. The page-grid extension is now
enabled only when that flag is set. Ordinary author-sized boxes retain their declared decoration
height, while the issue-#569 fallback and the separate continuing-column path remain unchanged.

## Evidence

- `PositionedChildDecorationExtentTests.InFlowChildOverflowingDefiniteHeight_DoesNotGrowTheDecorationRect`
  asserts both that the child genuinely overflows and that the parent's painted fragment stays at its
  declared 40pt height.
- Both issue-#569 flex-item decoration tests still pass.
- The affected FragmentEmitter, fixed-position, line-height, positioned-child, and Acid2 groups pass:
  123 tests on `net8.0`.
- Regenerated Acid2 output in both PDFium and MuPDF now gives the lower smile row the same 6em width
  as Chrome.
- The full `net8.0` suite passes: 11,404 passed, 0 failed, and 9 platform skips. Diff coverage
  against `origin/main`, including working-tree changes, is 99%.
- `dotnet build PeachPDF.slnx -t:Rebuild` completes with 0 warnings and 0 errors.
