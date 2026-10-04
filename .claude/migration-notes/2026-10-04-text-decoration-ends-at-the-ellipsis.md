# A text decoration on a truncated line no longer runs under the ellipsis

**Before:** on a line truncated by `text-overflow: ellipsis` (with `overflow: hidden`), an `underline`,
`overline` or `line-through` was drawn under the ellipsis and the room after it, out to the edge of the
box. This was the case for a decorated block and for a decorated inline (a link) inside the truncating
block alike.

**Now:** the decoration covers the kept text only and ends where the ellipsis begins, as in browsers.
A decorated inline that falls wholly past the cut draws no line. A line that fits is unchanged.

**Also changed:** when the first word of an inline box on a truncated line is dropped whole but an
earlier sibling box on that line was kept, the ellipsis now follows the kept sibling instead of being
drawn at the block's content-start edge.

**Why:** css-text-decor-3 decorates the box's inline content, and the truncated part is no longer any
of it.
