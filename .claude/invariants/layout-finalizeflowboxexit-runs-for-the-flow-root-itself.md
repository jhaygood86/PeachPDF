# `FinalizeFlowBoxExit` runs for the flow root itself, so an atomic-inline correction must exclude `blockBox`

`CssLayoutEngine.CreateLineBoxes` flows every block, grid item, flex item and table cell that holds inline
content as `FlowBox(blockBox, blockBox)` - the box is both the thing being flowed **and** the flow root.
`FinalizeFlowBoxExit(box, blockBox, ...)` is that call's per-box epilogue, so it runs for the root as well
as for every nested box, and its `opensHere` is true for the root.

**Any correction in there that exists for an atomic inline-level box (inline-block/inline-table) must say
`box != blockBox`.** For the root:

- `startY` is the **content** edge (the caller pre-shifts by the root's own border-top + padding-top), not
  the border edge an atomic inline's `trueStartY` reconstructs;
- the root's own declared `height`/`min-height`/`max-height` is applied afterwards by
  `ApplyHeight`/`GetBoxHeight`, and its bottom padding + border is added to `MaxBottom` after this
  epilogue returns (`blockBox.ActualBottom = coordinates.MaxBottom + padBottom + borderBottom`).

So a border-box answer such as `ResolveAtomicInlineDeclaredHeight` compared against a content-anchored
advance counts the vertical padding and border **twice**. Note `min-height`'s initial `0` is a valid
length: "declared" is at least the padding + border for every box, so nothing has to be declared for it to
misfire.

Measured symptom (PR #1175 regression, fixed since): `.cell{padding:10pt;font-size:10pt}` one-line block,
grid item, flex item and table cell all 40.0pt instead of 32.75 (single line 12.75 + 20 padding); a
content-box `min-height:40pt;padding:10pt` card 80 instead of 60; a `td{height:30pt;padding:8pt}` 62
instead of 46. Blocks with an explicit `height` looked fine (`ApplyHeight` overwrites), which is why the
grid/flex tests that all used `height:20pt` stayed green - a test for this area needs an **auto-height**
padded box, and should measure against an unpadded twin in the same document. Pinned by
`BlockLevelPaddingSingleLineHeightTests`.
