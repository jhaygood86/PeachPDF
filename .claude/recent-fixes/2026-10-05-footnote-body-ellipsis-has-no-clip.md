# A `float: footnote` body with `text-overflow: ellipsis` threw while painting (#1640)

`PaintWordsWithEllipsis` took the containing block's content edge from `OverflowClipOf(g, fragment).Clip!.Value`.
A fragment's `OverflowClip` is its clipping **ancestor's**, so it is populated for words that sit under a
truncating block - and null for a box that is its own truncating block with no clipping ancestor above it.
A footnote body is exactly that: a detached root (display `block`, so `ClipsItsOverflow` is not the cause),
painted on its own by `PdfGenerator` (`painter.PaintFragment(g, body)`). The `!` threw
`NullReferenceException`.

`ResolveEllipsisGeometry` now returns null when there is no clip, and both callers (the word painter and the
decoration's `CutOfFragmentOnLine`) fall back to what they did without ellipsis: untruncated words, no cut.
One guard in one place, rather than a precondition each caller has to remember. Honouring the ellipsis
inside a footnote body would need the body's own padding edge; recorded as an accepted gap (#1641).

The test (`FootnoteEllipsisTests`) paints a body the way `PdfGenerator` does. `FragmentPaintHarness.PaintPage`
never reaches it, which is why the crash was invisible to the suite: paint footnote bodies explicitly.
