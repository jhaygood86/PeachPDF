# A `clip` axis no longer clips the other axis

Before: `overflow-x: clip` (or `overflow-y: clip`) beside a `visible` axis cut the box's content on both axes.
After: only the `clip` axis cuts, as in browsers; overflow in the other direction shows.
