# A multi-column container resumed onto a float continues the float instead of throwing (#1486)

`CssLayoutEngineColumns.PerformLayout` resolved a resumed pass's first child with
`children[FirstChildIndexOf(resume, children)]`. `children` is the *in-flow* list (floats, abspos and `display:none`
are filtered out), while the break token's `ResumeChildIndex` indexes the container's whole `Boxes`. When the earlier
fragment left only a float for the next page, the token named the float, the lookup missed, `PrecedingRealChildren`
returned `children.Count`, and the indexer threw `ArgumentOutOfRangeException` (wrapped as "Failed multi-column layout").

**Fix:** a resume index past the in-flow children means only out-of-flow content is left, so the pass skips the column
machinery and runs `FillFragmentainerWithBlockChildren(g, resume)` over the container narrowed to its first column
(`PlaceColumn`, restored in a `finally`), then `LayoutOutOfFlowChildrenAgain`.

**Trap found by running it:** the first attempt reused `LayoutOutOfFlowChildrenOnly`, which lays the float out from
scratch. That removed the crash but drew the float's first lines on both pages (a doubled word). The resume record has
to reach the float: `LayoutBlockChildren` calls `childBox.ResumeAt(...)` for the child the token names, and the
from-scratch helper never does. A test that only checks "does not throw" would have passed the broken version, so the
test compares every painted word against the markup (`PaintedWords.Diff`) for both lost and doubled words.

Evidence: `MulticolContentLossTests`; multicol/column/float filter of the suite (858 tests) passes on net8.0.
