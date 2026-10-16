# A replaced flex/grid item no longer gets the line-height strut (issue #1666)

**Symptom.** A 96x48px `<img>` (36pt tall) that is a flex or grid item made its row 38.3pt+ tall (38.82pt
with the reference font), in a grid with `align-items:center` shifting everything below it.

**Cause.** The blockify change ([inline-level flex/grid items](2026-10-08-inline-level-flex-and-grid-items-are-blockified.md))
leaves a replaced item as the item itself instead of wrapping it. The item is therefore its own block
container whose only content is its phantom word, and `LineBoxContributionOf` reserved the block's
line-height strut for that line. The strut was already skipped for the `IsReplacedBlockWrapper` of a
`display:block` image ([block-display replaced elements](2026-09-16-block-display-replaced-elements-no-longer-get-a-strut.md));
the unwrapped item just was not covered.

**Fix.** `LineBoxContributionOf` also skips the strut when the block box is itself a `CssBoxImage` or
`CssBoxSvg`. An inline image is never its own line's block box outside this case, so nothing else changes;
an image inside a text item keeps its strut (pinned).

**Evidence.** `ReplacedItem_DoesNotGrowItsRowByTheLineHeightStrut` fails on the merge base (38.3 against 36)
and passes now. Two older tests asserted the scaled 52.8pt/38.8pt row sizes and moved to their strut-free
50pt/36pt equivalents. The gap note for auto grid rows no longer cites 38.8pt as the expected value.
