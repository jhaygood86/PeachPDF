# A float after an inline-block differs from a browser in a few shapes (#2227)

A left float after an inline-block with no block-level content now moves the box beside it when it fits and goes below the line when it does not (both as in
Chromium: box at x=100, or the float at y=83 under a 250px float). What is left:

- An inline-block that holds block-level content, and `inline-table`, `inline-flex` and `inline-grid`, still send a float over them below the line even where
  they would fit beside it (`FloatOverlapsLineBox`): moving a box with content of its own needs its whole subtree moved.
- The right-float rule stops at the box's siblings: an inline-block inside an earlier inline wrapper that holds block content does not send a right float below the line.
- The float is placed below the line's bottom as it stands when it is met, so content after it that makes the line taller is not counted, and an inline-block with
  a `margin-top` lands about 20px short of Chrome with a 10px margin.
