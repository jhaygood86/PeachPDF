# `pointer-events: none` takes a link out of the PDF

Before: `pointer-events` was ignored, so an `<a href>` (or an element inside one) always became a link
annotation, even under `pointer-events: none`.

Now: an `<a>` whose computed `pointer-events` is `none` writes no link annotation (pointer-events is
inherited, so everything inside it is covered too; a descendant that sets `auto` again is not). The same
holds for the links PaintLinks hands to any other canvas. Documents that do not use `pointer-events` are
unchanged.
