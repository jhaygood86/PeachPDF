# An inline's outline goes around taller descendant boxes

Before: the outline of an inline element followed its own line rectangles, so an image or inline-block
taller than the line was cut through by the ring.

Now: the ring encloses those boxes too, as in Chromium. Content that fits inside the line is unchanged.
