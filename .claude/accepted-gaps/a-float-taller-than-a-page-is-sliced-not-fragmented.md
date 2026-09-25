# A float taller than a page is sliced, not fragmented

_CSS Fragmentation Level 3 §4.1/§4.4 (floats participate in fragmentation and carry on to the next
fragmentainer). Tracker: [#317](https://github.com/jhaygood86/PeachPDF/issues/317)._

A `left`/`right` float is laid out unbroken (`CssBox.LayoutBlockChildUnbroken` for a block-level float,
`CssLayoutEngine.LayoutContentUnbroken` for one among inline content). One that straddles a page boundary
but fits on a page is moved whole to the next page (`CssBox.MoveWholeOntoTheNextPageIfItFits`). One taller
than a page runs on across pages, each page drawing its slice, and a line of the float that straddles a
page boundary is cut there, half on each page. The text beside it flows correctly onto each page.

The alternative, breaking the float between its lines, ended the layout pass inside the float, and every
in-flow line laid out beside it was placed back on the page already emitted: a block beside a 30-line
float drew only its last four lines (#1339). Real fragmentation needs the float's break to be resumed
independently of the in-flow token chain, which is #317.

A float holding a multi-column container is not laid out unbroken, because the columns engine needs the
fragmentainer that detaching removes; it still breaks between its lines, with #1339's loss for the text
beside it. A scroll container around such a float stays monolithic, since the multi-column container
fails its descendant check; one *beside* it, such as the `overflow: hidden` block of a media object, now
fragments like a plain block does and has the same loss. A float inside a column also keeps the breaking
path: laid out unbroken, its lines past the column's foot were drawn below the column, where no page shows
them, since a column does not continue a slice the way the next page does.

A forced break inside an unbroken float is not honoured, because the fragmentainer is detached. css-break-3
§3.1 requires break properties only in the fragmentation root's own flow and makes the rest optional ("may"),
so this is not a deviation; it is noted because the old breaking path did honour it.

Same slice-boundary mechanism as [the tall absolute box](a-tall-absolutely-positioned-box-is-sliced-not-fragmented.md)
and [#1328](a-capped-scroll-container-taller-than-a-page-loses-its-boundary-lines.md).
