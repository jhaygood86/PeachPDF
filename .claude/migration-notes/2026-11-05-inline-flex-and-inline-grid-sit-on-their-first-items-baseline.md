# An inline flex or grid container is aligned by its first item's baseline

**Before.** A `display: inline-flex` (or `inline-grid`) box beside text was aligned by its top edge, not a baseline: with padding or a height larger than the text, its label sat below the
surrounding text by the difference (a badge with `padding: 14px` put its text 9px below the words next to it).

**Now.** Its baseline is its first item's first line's (css-flexbox-1 section 8.5, css-grid-1 section 6.1), shared with the line like an inline-block's: the label lines up with the text beside
it and the box grows around that baseline. A container with no text in any item takes its bottom margin edge as its baseline, as a replaced element does. `vertical-align` values other
than `baseline` behave as before.

Not verified against `v0.9.26-preview1`.
