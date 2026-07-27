# The flex/grid relocation walk assumes line order is block-axis order

`CssLayoutEngineFlex.RelocateLinesAcrossFragmentainers` and `CssLayoutEngineGrid.RelocateRowsAcrossFragmentainers` walk their lines in list order and accumulate a running displacement, on the assumption that a later line is lower down the page. Two ordinary declarations break it, both measured on a 200pt page (160pt band):

- **`flex-wrap: wrap-reverse`** reverses the cross offsets after they are assigned, so the first line in the list is the *last* one down the page — two full-width 40pt lines with no break values at all put the first item at `y = 180` and the second at `y = 140`. The walk then accumulates a displacement onto lines physically above the one that moved, and pairs a `break-after` (issue #441) with the line really above it rather than below.
- **`flex-direction: column` with wrapping** stacks the lines along the *inline* axis: they sit side by side sharing one block-axis range, so there is no block-axis boundary between them for §3.1 to name. A `break-after: page` on the first line moved the second — at `(x 120, y 180)`, beside the first at `(x 20, y 140)` — onto the next page.

Pre-existing: the accumulation and the `break-inside`/monolithic relocation both had this before `break-after` was read at all. Filed as [issue #448](https://github.com/jhaygood86/PeachPDF/issues/448).
