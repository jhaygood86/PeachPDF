# `<br clear>` is honoured, and floats are placed higher

- `<br clear="left|right|all">` (and `br { clear }`) used to be ignored. The line after the break, and a block after a break at the end of its content, now start below the floats on that side.
- A `clear` inside a box now clears floats that come before one of its ancestors in the same formatting context (a heading with `clear: both` inside a `<div>` that follows a floated sidebar used to stay beside the float).
- A `fieldset` or a `button` with `display: block` contains its floats: the content after it starts below them.
- A float that did not fit beside the floats on its row used to go below the lowest of them; it now goes to the first bottom edge that makes room (beside an earlier float once a taller one on the other side ends), as a browser places it.
- A float that follows a block inside an inline element (`<span><div></div><div style="float:left">`) is placed after the block, not before it.
