# A word after an inline-block that already fills its line wraps

Text after an `inline-block` that is as wide as, or wider than, its block used to stay on the inline-block's line, past the
block's right edge. It now wraps to the next line, as a browser does. Layouts that relied on the old overflow (a deliberately narrow
box with an inline-block and a trailing word on one line) need `white-space: nowrap` on the container.
