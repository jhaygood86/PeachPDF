# `background`: deviations from a browser that are tracked but not fixed

Found while fixing the multi-layer `background` shorthand (see [the recent fix](../recent-fixes/2026-10-19-background-shorthand-layers-that-omit-a-longhand.md)). Each is a measured difference from Chrome (computed values,
or its print output rasterized beside PeachPDF's), identical before that fix, and left out of it on purpose: a layer-count or grammar change in a different part of the pipeline from the shorthand's per-layer join. Each has an issue.

- **A vertical-first three-value position is rejected** (#1842). `background-position: top 10px left` (and the shorthand with it) is dropped; CSS Backgrounds 3 §3.6 takes the two `&&` groups in either order. `left 10px top` is accepted.
- **A single `<box>` keyword sets only `background-origin`** (#1843). §2.10: one box value sets both origin and clip, two set origin then clip. `url(a) content-box` leaves the clip at `border-box`.
- **`/ <size>` is accepted with no position before it** (#1844). `background: url(a) / 10px` is valid here; the grammar is `<bg-position> [ / <bg-size> ]?`, so a browser drops the declaration.
- **`background: inherit` and `background-image: inherit` do not paint the inherited image** (#1845). The child draws the inherited colour only.
- **A `none` entry in `background-image` shifts the layer index of the images after it** (#1847). `ParseImages` filters `none` out of the list, so `background-image: none, url(a)` pairs `a` with layer 0's position, size and repeat. This is a painter
  gap, not a shorthand one (the longhands show it); it became reachable from the shorthand once `background: right bottom no-repeat, url(a) no-repeat` stopped losing its layer.
- **A two-value `background-repeat` is drawn as `repeat`** (#1848). `repeat no-repeat` and `no-repeat repeat` tile in both directions; §3.4 makes them `repeat-x` and `repeat-y`. The keyword forms are right.

Not deviations: `background-attachment: fixed` matches a browser's print output (it is relative to the page box); a screenshot of a browser, which is relative to the viewport, differs, and is not the reference for a PDF.
