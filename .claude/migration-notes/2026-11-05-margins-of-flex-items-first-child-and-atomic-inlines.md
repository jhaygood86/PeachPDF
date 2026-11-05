# A flex or grid item keeps its first child's top margin inside it; atomic inlines count their vertical margins

**Before.** (1) In `display: flex` / `grid`, an item whose first child had a top margin (`<div class="flex"><div><h2 style="margin-top: 12px">`) was placed that margin lower than it
should be and was that margin too short, so what followed overlapped by the margin. (2) An `inline-block`, `inline-flex` or `inline-grid` box with `margin-top` or `margin-bottom` got a line
no taller than without the margin.

**Now.** (1) The item is as tall as what it holds, with the child's margin inside it, as in browsers (a flex or grid item is its own formatting context). (2) The line holds the box's margin
box: a button with `margin-top: 20px` in an otherwise empty line makes it 20px taller and sits 20px below the line's top. Negative margins are unchanged. Pages laid out as flex/grid cards with
a headline margin, or with spaced buttons, come out taller than before and match a browser's height; PDF output with such content changes the same way.

Not verified against `v0.9.26-preview1`.
