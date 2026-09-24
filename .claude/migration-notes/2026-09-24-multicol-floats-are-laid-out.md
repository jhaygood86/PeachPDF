# A float in a multi-column container is laid out in its column

**Before:** a container whose children were all floats (`float: left`/`right`, or a page float) left every
float at zero size and position, and the container had no height from them. A `float: right` inside a column
was placed at the container's right edge, in the last column. A `float: right` at the top of the first column
also shortened the lines at the same height in the later columns to one word each.

**Now:** the floats are laid out (in the first column when they are all there is), a `float: right` sits at
its own column's right edge, and other columns' lines are unaffected. The container's automatic height grows to
cover its floats, as any formatting-context root's does. A float wider than its column overflows it.

**Also:** a multi-column container holding bare text beside a float used to lose the text and now shows it (in
one column, as bare text without a float already did).
