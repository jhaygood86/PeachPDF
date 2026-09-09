# A trailing `<br>` no longer adds a line

A `<br>` with nothing after it in its block used to leave an empty line behind it, making the block
one line taller than a browser renders it. It now ends the line it falls on and opens none. CSS 2.1
[§9.4.2](https://www.w3.org/TR/CSS21/visuren.html#inline-formatting) requires a line box holding no
content to "be treated as not existing for any other purpose".

| markup | before | now, and in a browser |
| --- | --- | --- |
| `x<br>` | 2 lines | 1 line |
| `<br>` | 2 lines | 1 line |
| `x<br><br>` | 3 lines | 2 lines |
| `x<br>` then a `display: block` sibling | 2 lines + the block | 1 line + the block |
| `x<br><br>c` | 3 lines | 3 lines — unchanged |

**When a document author would notice.** Any block whose inline content ends in a `<br>` is now one
line shorter. The shape that shows it most is a table cell holding a label above an image —
`label<br><img style="display:block">` — where every row of the table loses a line's height, and a
long table can gain back a page. Content that relied on a trailing `<br>` for spacing will close up;
use a margin, or a second `<br>` (which still leaves one empty line, as it does in a browser).
