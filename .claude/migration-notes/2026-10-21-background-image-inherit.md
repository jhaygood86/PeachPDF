# `background: inherit` and `background-image: inherit` now paint the inherited image

A child with `background: inherit` (or `background-image: inherit`) got the parent's colour, position, repeat and size but no image: the inherited layers were silently dropped. The child now paints its own copy of the parent's image layers (URL images and gradients), as in a browser.
