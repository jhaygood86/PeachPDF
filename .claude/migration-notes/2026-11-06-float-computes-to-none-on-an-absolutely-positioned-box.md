# An absolutely positioned box with `float` no longer moves the text beside it

A box with `position: absolute` or `fixed` that also has `float: left`/`right` (a leftover `float` in a stylesheet, a utility class applied to a positioned element) used to narrow the lines of the text beside it as a float does: the text started to the right of the box's width, over the space the box was not taking. The computed `float` is now `none` there (CSS 2.1 §9.7), so the text starts where it would without the box. The box itself is placed as before, by its insets.
