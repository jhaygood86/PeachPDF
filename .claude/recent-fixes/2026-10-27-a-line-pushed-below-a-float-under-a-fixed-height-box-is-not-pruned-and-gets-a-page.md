# A line pushed below a tall float, under a fixed-height box, was drawn on no page (#1965)

Before: a block of fixed height beside a tall left float, inside a right float, had no room for its word, so the line was
placed below the float, far under the block's own box (`height: 44pt` at y=28; the line at 340). Its text was in no page's
fragment tree. Two separate causes, found with the pruning parity check and a sweep of the float's height:

1. **Pruning.** `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` threw at slot 2: the outer float had one child fragment without
   pruning and none with it. `SettledBottomOf` judged a block by its `ActualBottom`, so the block (bottom 72) was
   concluded to be behind slots 1 and 2 while its line still sat in slot 2. Now a box with a height or max-height of its
   own is judged by `CssBox.GetMaximumBottom`, which includes its lines and its descendants'. Boxes with an auto height
   are unchanged: their `ActualBottom` already covers their lines. Fixed most heights of the sweep (all but those where
   the float ended at or just past a page foot).
2. **No page for it.** Where the float ends at a foot (heights of 13, 26, 39, 52 lines with no padding, 12 with 8pt) the
   line lands on a page nothing else reaches: `LastSlotAnyGeometryTouches` asked only `ActualSize`, which grows by boxes'
   bottoms. It now also takes the lowest line bottom in the tree (`LowestFlowedLineBottom`), skipping what is painted per
   page or clipped by its box, and so emits that page.

What was misread: the first read of the page count suggested the float's tail needed a third page and that the pass driver
was dropping it; `LastSlotAnyGeometryTouches` was already returning 2 there. The pages were being emitted and the block was
being skipped inside them.

Evidence: `ABlockBesideATallNestedFloat_KeepsItsLineThatIsPushedBelowTheFloat` and
`ALineBelowATallFloatInsideAFloat_GetsAPageWhereverTheFloatEnds` (float heights of 14 to 40 lines, with and without
padding) fail without the change; Engine.Tests 16536 and PeachPDF.Tests 1499 on net8.0 (Debug), no page counts changed.
