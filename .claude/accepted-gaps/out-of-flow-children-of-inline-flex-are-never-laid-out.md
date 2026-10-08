# Out-of-flow children of an `inline-flex` container are never laid out

[css-flexbox-1 §4.1](https://www.w3.org/TR/css-flexbox-1/#abspos-items): an absolutely-positioned child of a
flex container does not take part in flex layout and is positioned against its containing block.
`display: inline-flex` is the same container, only inline-level.

PeachPDF lays out the out-of-flow children of a block-level flex container in `CssBox.LayoutEngineContent`,
after the flex engine runs. An `inline-flex` box goes through `CssLayoutEngine.FlowInlineFlexChild`, which
calls `CssLayoutEngineFlex.PerformLayout` directly and never reaches that step, so an `position: absolute`
or `fixed` child of an `inline-flex` box stays 0x0 at the origin. A `<div>` child has never been laid out
there on any version.

**What changed with blockifying flex/grid items** ([recent fix](../recent-fixes/2026-10-08-inline-level-flex-and-grid-items-are-blockified.md)):
an absolute `<img>`/`<svg>` child used to be wrapped by `CorrectReplacedElementBoxes` in an in-flow block
(`IsReplacedBlockWrapper`), and the wrapper was laid out through ordinary flow, so the image happened to
paint. Skipping that wrapper for a direct child of a flex/grid container (which is what makes it the item
itself, and what fixes an out-of-flow image taking part in the flex line of a block-level `flex`
container) means an absolute/fixed `<img>`/`<svg>` in an `inline-flex` box is now not painted either.
`flex` and `grid` containers, and `inline-grid`, are unaffected.

**Why it was left.** The root cause is older than the change and the visible loss was judged out of scope
for it. A fix was tried on a scratch tree and worked: extract the "detach the fragmentainer, then
`LayoutOutOfFlowChildren`" step that `LayoutEngineContent` ends with into an internal `CssBox` method and
call it from `FlowInlineFlexChild` right after `CssLayoutEngineFlex.PerformLayout`. It was not verified
against the fragmentation and page-break paths.

Filed as [issue #1665](https://github.com/jhaygood86/PeachPDF/issues/1665), which carries the suggested fix.
