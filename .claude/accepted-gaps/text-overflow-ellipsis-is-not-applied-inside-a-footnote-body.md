# `text-overflow: ellipsis` is not applied inside a `float: footnote` body

Tracked as [#1641](https://github.com/jhaygood86/PeachPDF/issues/1641).

A footnote body that is its own truncating block (`overflow: hidden; text-overflow: ellipsis`) paints its text
untruncated. The ellipsis boundary is taken from the fragment's `OverflowClip`, which is the clipping
**ancestor's** clip; a footnote body is a detached root with no clipping ancestor and the truncating block is
the body itself, so there is no clip to read (`ResolveEllipsisGeometry` returns null).

**Spec rule:** css-overflow-4 `text-overflow` applies to block containers, a footnote body included.

**Why out of scope:** the fix for the crash (#1640) stops at not throwing. Truncating there needs the body's own
padding edge (its fragment rectangle less its borders) made available to the painter for the case where the
truncating block is the fragment's own box.

User-facing note: the `text-overflow` row in `docs/html-css-support.md`. Closing the gap means deleting this file
and that sentence.
