# `background-blend-mode` does not apply to `@page` and margin-box backgrounds

`FragmentPainter.PaintBackground` builds the isolated group and blends each layer; `LayeredBackgroundPainter` (the `@page` box and page-margin-box backgrounds) draws its layers straight, so the property has no effect there. Tracked in issue 2275.
