# A `float: footnote` body with `text-overflow: ellipsis` threw while painting (#1640)

`PaintWordsWithEllipsis` took the containing block's content edge from `OverflowClipOf(g, fragment).Clip!.Value`,
which is populated for words reached by walking the page. A footnote body is painted on its own by `PdfGenerator`
(`painter.PaintFragment(g, body)`), and the fragment of its words carries no clip, so the `!` threw
`NullReferenceException`.

What was established by dumping the fragment tree of a painted body: the body (`display: block`, so
`ClipsItsOverflow` is not the cause) has an anonymous child holding the words, whose containing block is the body
(`TextOverflow == Ellipsis`), and that child's `OverflowClip` is null. Why the emitter records no clip for it was
not chased down - the child's rectangle is also unplaced (X=0, wide 0), so footnote-body geometry is worth a look
before anyone tries to honour the ellipsis there. A first attempt to derive the edge from the fragment's own box
failed for exactly that reason (the words' fragment is the child, not the body), and was dropped.

`ResolveEllipsisGeometry` now returns null when there is no clip, and both callers (the word painter and the
decoration's `CutOfFragmentOnLine`) fall back to what they did without ellipsis: untruncated words, no cut. One
guard in one place, rather than a precondition each caller has to remember. Honouring the ellipsis inside a
footnote body is recorded as an accepted gap (#1641).

The test (`FootnoteEllipsisTests`) paints a body the way `PdfGenerator` does. `FragmentPaintHarness.PaintPage`
never reaches it, which is why the crash was invisible to the suite: paint footnote bodies explicitly.
