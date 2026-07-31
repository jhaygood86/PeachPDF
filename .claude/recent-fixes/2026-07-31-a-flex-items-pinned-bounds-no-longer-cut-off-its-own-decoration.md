# A flex item's pinned bounds no longer cut off its own decoration

Closes [#569](https://github.com/jhaygood86/PeachPDF/issues/569), filed the same day as
[`2026-07-31-a-detached-measurement-pass-can-no-longer-take-a-page-break.md`](2026-07-31-a-detached-measurement-pass-can-no-longer-take-a-page-break.md)
while diagnosing that unrelated pagination bug. A flex item whose own content is entirely block-level
children — no direct text on the item itself, e.g. a `margin-top:auto`-anchored `<footer>` holding only
`<p>` children, the same pattern the `invoice` showcase uses — lost its `background`/border on every
page past the first one its content genuinely spans.

## The load-bearing idea

`ItemContentCommit.CommitLayout` (`src/PeachPDF/Html/Core/Fragmentation/ItemContentCommit.cs`) pins a
flex/grid item's content-box `Width`/`Height` once, on its first, fresh commit, and never revisits it on
a later, resumed one — deliberately, so a nested engine re-deriving its own content box from them sizes
consistently pass to pass. A box whose real content outgrows that pinned size (an explicit `height`
smaller than its children's true height, or — the `invoice` shape — a flex-shrunk height that a
`margin-top:auto` container computed before knowing the content would overflow past one page) still
fragments its overflow into later fragmentainers regardless; only its own *declared* bounds stay put.

`FragmentEmitter.BuildDraft` sets `usesOwnBounds = true` for a box with no per-line rectangles of its
own (all its content lives on children), and `ExtentOf` normally trusts that box's own `Bounds` as the
decoration rectangle for every fragment it appears in — correct for an ordinary continuing block, whose
`ActualBottom` genuinely grows to cover every page it spans. It is not correct here: the item's `Bounds`
stops at its pinned bottom, so a later fragment's decoration rectangle either missed the fragmentainer
entirely (zero `Lines`) or, sharper, landed a small *sliver* inside it — a background/border sized to a
few points of overlap while the actual children ran on for a full page or more beneath it (visually, a
border cutting through the middle of a paragraph, with everything after it undecorated).

The fix reuses the mechanism `BoundsEndAtItsContent` already provides for a nested fragmentainer's
continuing box (whose own bounds are zero-height until its epilogue runs): a page-grid box that
`usesOwnBounds`, isn't a shell, and holds real children/words in this fragmentainer now also gets
`BoundsEndAtItsContent = true`, so `ExtentOf` extends its bottom to cover whatever those children
actually reach. Unconditional, not gated on the box's own bounds already missing the region outright —
`ExtentOf`'s extension only ever grows the bottom (`Math.Max`), so it is a no-op wherever the box's own
bounds already reach far enough, and a real fix wherever they land short, sliver or otherwise.

## What was found by running it, not by reading it

The first fixture tried (an explicit `height` well short of the content, zero overlap with the
continuation page) passed with a narrower version of the fix gated on `!ownBoundsCoverRegion`. A second
fixture — the item's pinned bounds straddling the page boundary by a few points rather than missing it
outright — still failed under that narrower gate: the sliver of overlap satisfied `ownBoundsCoverRegion`,
so the fallback never fired, and the fragment's decoration stayed sized to the sliver alone. Both shapes
are now covered by dedicated tests (`AFlexItemWithNoDirectText_KeepsItsDecorationPastItsOwnPinnedBounds`,
`AFlexItemWithNoDirectText_KeepsFullDecorationWhenItsPinnedBoundsOnlySliverIntoALaterPage`), and both
assert that the decoration rectangle actually *covers* the fragment's own content rather than merely
existing — a bare `NotEmpty` check on `fragment.Lines` passes even for a sliver-sized rectangle.

## Evidence

Full `net8.0` suite: 7365 passing, 9 skipped, 0 failed. Both new tests confirmed to fail without the
fix (the first with zero `Lines`, the second with a short decoration rectangle) and pass with it.
`dotnet build PeachPDF.slnx -t:Rebuild`: zero warnings. New showcase
`flex_item_pinned_height_background` added and regenerated through `PeachPDF.TestHarness`; rasterized
with both MuPDF and PDFium at 2x scale and visually confirmed the footer's background and border now
wrap all eight paragraphs on page 2, not just the first.
