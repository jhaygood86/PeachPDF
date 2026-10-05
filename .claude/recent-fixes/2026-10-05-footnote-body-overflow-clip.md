# A footnote body (and a running element) records the `overflow` clip of its content (#1641)

**Cause.** `MarginBoxContentFragmentBuilder.Build` gave every fragment `OverflowClip: null` ("a running element's
content is never clipped"). It builds a `float: footnote` body (`AttachFootnoteAreas`) and the content of
`content: element()` margin boxes, so `overflow: hidden` inside either was never recorded: nothing was clipped, and
the ellipsis painter had no edge to read (it threw until [the crash fix](2026-10-05-footnote-body-ellipsis-has-no-clip.md),
then painted untruncated).

**Fix.** `Build` carries the nearest clipping ancestor's clip down to each child, the way the page emitter does:
a fragment carries its clipping *ancestor's* clip (the painter pushes it before drawing the fragment's content),
so the root has none and a box that clips passes its own padding edge to its children. The derivation is shared:
`RenderUtils.OverflowClipGeometryOf` (padding edge, one-axis opening, corner curve) was lifted out of
`FragmentEmitter.OverflowClipOf` so the two builders cannot disagree.

**Detached trees need a root for the decoration cut.** A decoration finds its line's `text-overflow` cut by indexing
the page's fragment tree, and a footnote body or margin box is painted on its own (`_pageRoot` is null), so the
underline was not shortened. `PaintDetached` sets `_detachedRoot` for the call and `PdfGenerator` uses it for both
detached paints; `_pageRoot` is deliberately not reused, since the backdrop and flatten painters branch on it.

**Not done.** `position: absolute`/`fixed` boxes inside the tree are not built at all (`!b.IsOutOfFlow`), so the
chain never has to jump to another containing block; that is unchanged.

**Evidence.** `FootnoteEllipsisTests` (truncation, clip, border inset, underline), `MarginBoxContentClipTests`
(running element: descendant carries the clipper's padding edge, root carries none, no `overflow` means no clip).
Footnote showcase section rasterized in PDFium and MuPDF.
