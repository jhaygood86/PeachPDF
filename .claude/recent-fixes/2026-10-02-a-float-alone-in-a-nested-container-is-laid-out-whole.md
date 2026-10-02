# A float alone in a container nested in a column lost its later lines (seeds 40, 248, 252)

`LayoutOutOfFlowChildrenOnly` lays out a multi-column container that holds only floats: with no in-flow content there
is nothing to put in columns, so the float is laid out in place. Nested in an outer column, that happens while the outer
column's fragmentainer is current, so the float's own line flow broke at the outer column's band (the balance trials
start at an even share of the content, a couple of lines). The float is not an in-flow child, so no carry reaches the outer
fill, nothing retries with a taller band and nothing ever resumes the float: the lines past the band were laid out for no
fragmentainer, kept in the tree by no instance and drawn nowhere. At the top level the current fragmentainer is the page,
and the same document lost nothing.

Fix: when the container is nested in a column (`CurrentFragmentainer.HasOwnBand`), detach the fragmentainer while the
floats are laid out (the way the measurement pass detaches it), so the float is laid out whole and overflows the column
foot, as a float that does not fit in its column already does (see the accepted gap on that).

Reduction: `columns:3` > `columns:3` > a `float:right` 54pt by 46pt holding five words; lines three to five were lost.
Left floats and floats taller than the content too.

Evidence: corpus (300 documents, page-edge visibility) lost words 13 to 2, doubled unchanged at 6; seeds 40, 248 and 252
go to zero and none worsens. net8.0 suite green; the new test fails on main.

Not done: `w112_66` (a float wider than its column in nested columns) and `w214_29` (a word at y=228, near the page
foot) are still lost.