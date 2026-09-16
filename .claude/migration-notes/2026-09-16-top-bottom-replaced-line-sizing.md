# Top- and bottom-aligned replaced elements no longer inflate line boxes

Previously, an inline replaced element using `vertical-align: top` or `vertical-align: bottom` could
increase its line by both its full margin-box height and the font strut extent on the opposite side
of the baseline. This could shift the element and following content downward. The behavior was
present in v0.9.18's line alignment path and became visible as a red/yellow strip above Acid2's eyes
after line boxes gained full shared-baseline accounting.

Now these elements contribute their margin-box height exactly once. Their margin edge is aligned to
the requested line edge, and the result does not depend on whether taller baseline-aligned content
appears before or after them.
