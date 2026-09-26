# A `break-inside: avoid` block taller than a page can lose a line

_CSS Fragmentation Level 3 §4.4 (content must not be lost when a box that cannot fit has to break).
Tracker: [#1369](https://github.com/jhaygood86/PeachPDF/issues/1369)._

Nested `break-inside: avoid` blocks that together are taller than a page can lose a line of text at the
page boundary: the line is drawn on no page. The reduced repro in #1369 has no `overflow`, float or
multi-column container, and loses `w95` on `main` at 781740b6.

Found by the content-preservation fuzz for #1321 (auto-height scroll containers fragment). A wrapper
breaking across pages inside such a block lost the lines at the boundary (fuzz seeds 238 and 273), and
the same document with the wrapper replaced by a plain `div` loses the same lines on `main`. So the loss
is in the relaxation that breaks the too-tall avoid box, not in the wrapper.

#1321 does not fix it. `MonolithicContent.AvoidsBreakInside` keeps a scroll container whole when it,
or any ancestor, has `break-inside: avoid`/`avoid-page`, which is the layout before #1321 and does not
reach this path. The user agent's `thead, tfoot { break-inside: avoid }` counts too, so a scroll
container in a repeating table header or footer stays whole.
