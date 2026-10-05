# A `float: footnote` body with `text-overflow: ellipsis` threw while painting (#1640)

`PaintWordsWithEllipsis` took the containing block's content edge from `OverflowClipOf(g, fragment).Clip!.Value`,
which holds whenever the fragment is reached by walking the page: the clipping ancestor is on the path.
`PdfGenerator` paints each footnote body on its own (`painter.PaintFragment(g, body)`), so there is no
ancestor, `Clip` is null and the `!` threw `NullReferenceException`.

`PaintWords` now falls back to the ordinary, untruncated word paint when that clip is null. Honouring the
ellipsis inside a footnote body would need the body's own padding edge available to the painter; not done.

The test (`FootnoteEllipsisTests`) paints a body the way `PdfGenerator` does. `FragmentPaintHarness.PaintPage`
never reaches it, which is why the crash was invisible to the suite: paint footnote bodies explicitly.
