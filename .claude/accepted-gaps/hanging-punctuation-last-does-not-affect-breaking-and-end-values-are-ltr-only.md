# `hanging-punctuation: last` does not take part in line breaking; the end values are left-to-right horizontal only

`force-end` and `allow-end` (a stop or comma ending a line) are left out of the fit test in `FlowBox`, and `force-end`/`last` are applied at line close by `ApplyEndHang`, which stores the hung advance in `CssRect.HungEndWidth`; `CssLayoutEngine.LineContentEnd` subtracts it so right/centre alignment and justification let the character overhang.

Two parts of CSS Text 3 §8.1 are not done:
- `last` hangs the closing bracket or quote of the block's last line only after the line has been broken, because the flow cannot know a word is the block's final one while it is still deciding whether it fits. A last line that overflows by exactly the closing character wraps rather than hanging it.
- Right-to-left lines and vertical writing modes are skipped; the end edge there is the physical left (or bottom) edge and `ApplyEndHang` returns early.

A border or padding closing the inline after the character blocks hanging, as the spec requires. Tracked in #2145.
