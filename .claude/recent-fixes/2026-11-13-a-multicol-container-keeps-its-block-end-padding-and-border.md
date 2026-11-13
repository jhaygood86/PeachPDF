# A multi-column container dropped its block-end padding and border

Found by the WPT runner (`multicol-rule-px-001` and the dozens like it that draw a bordered multicol box): the box was one border-width (or padding) too short, and its bottom border was painted over the content. Isolated with a scratch page: 8 words in 4 columns of `line-height: 16px` with `padding-bottom: 20px` was 32px tall, the same text in one column 52px.

## Cause
`CssLayoutEngineColumns.Layout` ended with `columnsBox.ActualBottom = max(boxTop, contentBottomOverall)` - where the columns end. The ordinary block path gets the box's own `padding-bottom` and `border-bottom` from `MarginBottomCollapse`, which this path never reaches, and `ApplyHeight` for an auto height keeps the content-driven bottom. The top edge was fine (the columns start at the content box), so only the bottom was lost.

## Fix
When the container finishes in this fragment (`carry is null`) its block-end padding and border are added to the bottom. A container that continues into the next fragmentainer gets them on its last fragment instead.

## Evidence
css-multicol 120 -> 148 passes, +29 across all modules, 0 lost; new `BlockEndPaddingAndBorder_SitBelowTheColumns` (box height asserted in points); engine, PeachPDF and PeachBrackets suites green.

## Not done
Whether the box's own bottom margin and a last child's escaping margin are right for a multicol container was not examined.
