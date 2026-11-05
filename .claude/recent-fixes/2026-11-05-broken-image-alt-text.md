# A broken image draws its alt text

`CssBoxImage` had no use for `alt` (it is read only for the accessibility tree): a failed load left `_imageWord.Image` null and the box took `MeasureIntrinsicSize`'s default placeholder.
`BrokenImageAlt` (alt, non-empty after trimming, only when the load was attempted from `src` and gave neither a raster nor an SVG) now feeds the text's measured size to
`MeasureIntrinsicSize` as the natural size with no ratio (so an author's `width`/`height` still win per axis), and `ImageFragmentPainter` draws the text clipped to the box.
Deliberately not done: wrapping the text inside a sized box (it is clipped on one line), the broken-image glyph and inset border some browsers add, and `<picture>`/`srcset` sources
that fail with no alt (unchanged: an empty box). Found as the "failed-image placeholder" item of the arstechnica.com comparison plan. Tests: `BrokenImageAltTextTests` and
`HtmlDocumentTests.AnImageThatCannotBeLoaded_ShowsItsAltTextOrNothing` (pixels).
