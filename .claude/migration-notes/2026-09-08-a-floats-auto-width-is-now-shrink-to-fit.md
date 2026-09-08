# A float's `auto` width is now shrink-to-fit

CSS 2.1 §10.3.5 makes a floating box's `auto` width shrink-to-fit —
`min(max-content, max(min-content, available))`. It was given the ordinary stretch-to-containing-block
width instead, so an auto-width float filled its containing block.

That leaves `float: right` nowhere to go: it landed at the left as a full-width bar, and the content
that belongs beside it was pushed onto its own line. A bordered badge tucked into the top right of a
cell rendered as a full-width band above the cell's text.

Such a float now shrinks to its content and lands where a browser puts it. Measured against Chrome
152, on a 300pt container:

| | Chrome 152 | before | after |
| --- | --- | --- | --- |
| auto-width `float: right`, offset into the container | 257.9pt | 0 (full width) | at the right edge |
| text after an auto-width `float: left` | beside it | on its own line | beside it |

A float with a **declared** width is unaffected — placement was never the problem, and a declared
width already landed on a browser's x to the point. An auto-width float is still never wider than
its containing block.

See [Display & Layout](../../docs/html-css-support.md#display--layout) in `docs/html-css-support.md`.
