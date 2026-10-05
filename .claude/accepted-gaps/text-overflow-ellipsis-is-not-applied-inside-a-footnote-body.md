# `text-overflow: ellipsis` is not applied inside a `float: footnote` body

Tracked as [#1641](https://github.com/jhaygood86/PeachPDF/issues/1641).

A footnote body with `overflow: hidden; text-overflow: ellipsis` paints its text untruncated, and since no clip is
pushed there either, a `white-space: nowrap` note can run past the body's width. The ellipsis boundary comes from
the words' fragment `OverflowClip`; for a footnote body's words it is null (`ResolveEllipsisGeometry` returns
null). The words sit on an anonymous child of the body whose containing block is the body, and the child's
rectangle is unplaced as well (X=0, width 0), so the body's geometry has to be sorted out before an edge can be
derived from it. Deriving the edge from the words' fragment's own box does not work: that box is the child.

**Spec rule:** css-overflow-4 `text-overflow` applies to block containers, a footnote body included.

**Why out of scope:** the fix for the crash (#1640) stops at not throwing.

User-facing note: the `text-overflow` row in `docs/html-css-support.md`. Closing the gap means deleting this file and
that sentence.
