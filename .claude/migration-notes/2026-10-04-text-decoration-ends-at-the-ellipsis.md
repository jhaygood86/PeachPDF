# A text decoration on a truncated line no longer runs under the ellipsis

**Before:** on a line truncated by `text-overflow: ellipsis` (with `overflow: hidden`), an `underline`,
`overline` or `line-through` was drawn under the ellipsis and the room after it, out to the edge of the
box. This was the case for a decorated block and for a decorated inline (a link) inside the truncating
block alike.

**Now:** the decoration covers the kept text only and ends where the ellipsis begins, as in browsers
(which do not decorate the ellipsis glyph). A decorated inline that falls wholly past the cut draws no
line. A line that fits is unchanged. A box narrower than the ellipsis itself draws no line where a
browser underlines the clipped glyph.

**Also changed - where the ellipsis lands when a box's first word is dropped whole.** The ellipsis
anchor used to be the block's content-start edge. It is now the later of that edge and the dropped
word's own start, which moves it in three situations, with or without any decoration:

- **A kept sibling box before it** (`<a>ab</a><span>WWWW…</span>`): the ellipsis now follows the
  sibling instead of sitting at the start of the line. This is closer to a browser, but the sibling is
  kept without reserving room for the ellipsis, so it can run past the box edge and show as a partial
  dot, or be clipped away entirely, where a browser would shorten the sibling to make room.
- **`text-indent` with nothing kept:** the ellipsis moves from the content edge to the indent. With a
  small box it can end up past the edge or clipped away.
- **RTL:** on a right-to-left line the ellipsis can move from the visible right edge to the left edge,
  off the box or clipped, where it used to show one.

**Why:** css-text-decor-3 decorates the box's inline content, and the truncated part is no longer any
of it. The anchor change was needed so a decoration is not erased from a kept sibling.
