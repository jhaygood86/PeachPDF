# A `float: footnote` body ignores `overflow: hidden` and `text-overflow`

Tracked as [#1641](https://github.com/jhaygood86/PeachPDF/issues/1641).

A footnote body with `overflow: hidden; text-overflow: ellipsis` paints its text untruncated, and no clip is pushed
either, so a `white-space: nowrap` note can run past the body's width - `overflow: hidden` itself is not honoured
there, which is the larger half of the gap. Cause: `MarginBoxContentFragmentBuilder.Build`, which builds a footnote
body's fragments (and running-element margin-box content), gives every fragment `OverflowClip: null`, so the
ellipsis painter has no edge to read (`ResolveEllipsisGeometry` returns null) and nothing is clipped. The fix belongs
in that builder - record the nearest clipping ancestor's padding edge for descendants - and also changes how
running-element content clips, so it was not folded into the crash fix.

**Spec rule:** css-overflow-4 `text-overflow` applies to block containers, a footnote body included.

**Why out of scope:** the fix for the crash (#1640) stops at not throwing.

User-facing note: the `text-overflow` row in `docs/html-css-support.md`. Closing the gap means deleting this file and
that sentence.
