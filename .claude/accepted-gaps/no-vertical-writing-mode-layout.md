# `writing-mode` doesn't affect real layout (line flow, glyph rotation, table/flex axis)

Tracking issue: [#547](https://github.com/jhaygood86/PeachPDF/issues/547).

CSS Writing Modes Level 4 defines `writing-mode: vertical-rl` / `vertical-lr` / `sideways-rl` /
`sideways-lr` as rotating which axis is inline and which is block for a box's whole layout: line boxes
stack along a vertical block axis, glyphs orient per
[§text-orientation](https://www.w3.org/TR/css-writing-modes-4/#text-orientation), and both
[flex](https://www.w3.org/TR/css-flexbox-1/#writing-mode) and
[table](https://www.w3.org/TR/css-tables-3/) layout reinterpret their main/cross axes accordingly.

`writing-mode` parses, cascades, and inherits correctly (`WritingModeProperty`), and correctly drives CSS
Logical Properties resolution (`LogicalPropertyResolver`, `CssBox.ResolveLogicalProperties`) — a
`margin-block-start` under `writing-mode: vertical-rl` resolves to the physical right edge, matching the
spec's abstract-to-physical mapping table exactly. `text-orientation` (`TextOrientationProperty`) parses,
cascades, and inherits the same way. But no layout or paint code branches on a box's resolved
`WritingMode`/`TextOrientation` value: line-box flow (`CssLayoutEngine`), table row/column layout
(`CssLayoutEngineTable`), flex main/cross axis selection (`CssLayoutEngineFlex`), and glyph rendering all
stay unconditionally `horizontal-tb`-oriented regardless of the value.

A `WritingModeFrame` geometry utility (`src/PeachPDF/Html/Core/Utils/WritingModeFrame.cs`) and real
OpenType `vhea`/`vmtx`/`VORG` vertical-metrics table parsing (`src/PeachPDF/Fonts/OpenType/`) exist as
foundational plumbing for closing this gap, but neither is wired into any layout or paint code yet.

A document with `writing-mode: vertical-rl` therefore gets a spec-correct computed value and spec-correct
logical-property resolution, but its content still lays out in ordinary horizontal lines — no vertical
line-box stacking, no glyph rotation/uprightness, no flipped table/flex axis interpretation.

**Deliberately out of scope.** This was an explicit, scoped-down decision made when `writing-mode` support
was added: implement cascade + logical-property correctness only, not real vertical text layout — the
latter is a large, separate layout-engine project touching `CssLayoutEngine`, `CssLayoutEngineTable`,
`CssLayoutEngineFlex`, fragmentation, and the paint layer, not a CSS-OM/cascade fix.

The reader-facing note is in `docs/html-css-support.md`'s `writing-mode` row.
