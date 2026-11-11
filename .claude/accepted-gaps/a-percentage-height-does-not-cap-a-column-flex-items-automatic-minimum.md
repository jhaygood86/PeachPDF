# A percentage height does not cap a column flex item's automatic minimum size

`ResolveColumnAutoMinimums` (CssLayoutEngineFlex) skips a `height` that ends in `%`, so a column item with `height:30%` holding 150px of content in a 240px column keeps 150px as its automatic minimum (CSS Flexbox 1 §4.5 caps it at the specified size, 72px) and the other items of the line are shrunk to make room: 150/56.3/33.8 against a browser's 72/105/63. The row direction resolves a percentage `width` against the line's main size and gets this right; the column direction would resolve against the container's main size, but only when that is definite (a percentage `height` of an auto-height container computes to `auto`, which must then not cap the minimum), and `AlgorithmicDefiniteHeight`/`IsHeightDefinite` is the place that question is already answered for the rest of the engine. Left out of the change that added the row case to keep it to one direction.

Tracked in #2332.
