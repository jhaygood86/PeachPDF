# `<canvas>` had no box

A `<canvas>` is a replaced element with an intrinsic size of its `width`/`height` attributes, 300x150 by default (HTML Standard, canvas element). The UA stylesheet had no rule for it, so it was an empty inline box that ignored its size and showed only its border. `canvas { display: inline-block; width: 300px; height: 150px }` gives it the default; the attributes already map to `width`/`height` (`DomParser`'s presentational hints) and replace it.

css-flexbox `flexbox-flex-basis-content-004a/b` pass. Nothing is drawn *in* a canvas (there is no script); only its box and CSS background.

## Not done
`flexbox-flex-basis-content-001..003`: the canvas flex item still collapses to its border width (a `flex-shrink: 0; min-width: 0` item with `flex-basis: content`), and rows two and three of those pages differ in how a `width` is ignored under `flex-basis: content`.
