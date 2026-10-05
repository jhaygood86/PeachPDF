# A `float: footnote` body with `text-overflow: ellipsis` threw while painting (#1640)

`PaintWordsWithEllipsis` took the containing block's content edge from `OverflowClipOf(g, fragment).Clip!.Value`,
which is populated for words reached by walking the page. A footnote body is painted on its own by `PdfGenerator`
(`painter.PaintFragment(g, body)`), and the fragment of its words carries no clip, so the `!` threw
`InvalidOperationException` ("Nullable object must have a value"), surfaced as `HtmlRenderException: Exception in box paint`.

Cause, found by dumping a painted body's fragment tree and then reading how it is built: the body (`display: block`,
so `ClipsItsOverflow` is not the cause) has an anonymous child holding the words, with the body as its containing
block - and `MarginBoxContentFragmentBuilder.Build`, which `AttachFootnoteAreas` uses for a footnote body (and which
builds running-element margin-box content), gives every fragment `OverflowClip: null`. So the `overflow: hidden` of a
footnote body is never recorded for its content: no ellipsis edge, and no clip either. The proper fix is in that
builder (record the nearest clipping ancestor's padding edge for descendants), which also changes how running-element
content clips, so it is left to #1641. A first attempt to derive the edge from the words' fragment's own box failed
because that box is the anonymous child, not the body, and was dropped.

`ResolveEllipsisGeometry` now returns null when there is no clip, and both callers (the word painter and the
decoration's `CutOfFragmentOnLine`) fall back to what they did without ellipsis: untruncated words, no cut. One
guard in one place, rather than a precondition each caller has to remember. Honouring the ellipsis inside a
footnote body is recorded as an accepted gap (#1641).

The test (`FootnoteEllipsisTests`) paints a body the way `PdfGenerator` does. `FragmentPaintHarness.PaintPage`
never reaches it, which is why the crash was invisible to the suite: paint footnote bodies explicitly.
