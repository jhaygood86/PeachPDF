# A latch that bounds a decision must be reset per *layout*, not per document

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`ShrinkToFit` and the per-page reflow loop each re-run `LayoutDocument`.
