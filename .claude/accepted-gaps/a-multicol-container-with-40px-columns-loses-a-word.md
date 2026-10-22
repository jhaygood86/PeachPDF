# A multi-column container with 40px columns can lose a word (#1897)

**Gap:** `<div style="width:40px;columns:3">` holding a 40px-wide float, text, and a `float: none` block with `margin-top: -100px` draws three of its four words. `main` with the text inside a `<div>` draws all four.

**Why:** not found. The column is narrower than the floats and the words, so each word takes its own line, and one falls past the last column. It does not depend on the post-wrap float clearance. Degenerate: a column narrower than a single word.

**Not claimed:** that wider columns lose words this way.
