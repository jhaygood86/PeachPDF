# Replaced grid item sizing

Before: in a grid, a percentage `width`/`max-width` on an item resolved against the whole grid container; a
stretched image (`align-self: stretch`) was clamped to its grid area; `<iframe>` and poster-less `<video>` items
stretched across the area; an `<svg>` with only a `width` or only a `height` stretched in the other axis; and
`justify-self: baseline` behaved as `start`.

Now: percentages resolve against the item's grid area, so `width: 50%` is half the area and `max-width: 100%` is the
area; a stretched image can overflow its area unless it has a `max-width` (as in browsers); iframes and videos keep
a 300x150px size and one-sided svgs take 300x150px in the missing axis; `justify-self: baseline` lines items in a
column up on the largest start margin. A page that relied on the old clamp should add `max-width: 100%` to its
images.
