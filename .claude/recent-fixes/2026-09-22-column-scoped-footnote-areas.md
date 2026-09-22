# Column-scoped footnote areas, and the two things that make them work

`float: footnote` with css-page-floats' `float-reference: column` now routes a note to the foot of the
column its reference landed in. Two facts carried the whole feature, and one bug was introduced and
caught in the process.

## The mechanisms already existed

- **Stopping content above a column's foot** is `CssRect.WouldStraddleFragmentainer`'s
  `CurrentFragmentainer is { HasOwnBand: true }` arm, which already reads a band-end reservation. So a
  single `ReserveBandEnd` on the column's own `FragmentainerContext` is consumed correctly with no new
  fragmentation machinery at all.
- **Telling one column's fragments from another's** is already what `RecordCapturedInstance` does for
  the emitter, and it already computes every field a durable column identity needs. The new
  `ColumnFragmentainerRecord` is published beside it and truncated by the same two methods, so a
  balance retry discards both in lockstep by construction rather than by a second cleanup that could
  drift.

What this change adds is therefore a property, a per-column dictionary threaded through the
convergence loop the page-level one already uses, a partition step inside one existing method, and a
list where there was a nullable.

## The bug this introduced, and how it showed itself

Numbering had to move *before* partitioning (a column note must not restart the counter). But
`ApplyNumber` re-parses the call's words, which replaces its `CssRect`s with fresh ones sitting at
their unset defaults - so reading the call's anchor after numbering yields `(0, 0)`, and every
column-scoped note was silently attributed to whichever column happened to contain the origin. It did
not throw; it produced a plausible-looking wrong answer.

A probe test printing the actual anchors and column records is what found it, not reading. The fix is
to capture both coordinates in the one pass that runs before any call is renumbered - the same pass
that already groups calls by slot, and for exactly the same reason the existing code reads
`OwnGeometryTop()` rather than `Location.Y` there. **Anything that needs a footnote call's geometry
must take it in that pass.**

## Two decisions worth not re-deriving

- **`BandBottom` recorded for a column is the context's own `boxTop + target`, not the taller band the
  fill may record for an unbreakable child that overflowed it (css-multicol-1 §3.3).** The reservation
  is taken against the context band, so the area has to be placed against the same edge - otherwise an
  oversized child drags the note area down with it.
- **The `inset < target` guard on the reservation is load-bearing.** A reservation at or past the
  column's whole height leaves no usable space, nothing is placed, `carry` never advances, and the
  container defers page after page until `HasAlreadyBeenEntered` trips the monolithic last resort.
  Declining lets the area overflow the column, which is the same answer the page path already gives.

## Verified by rendering

The showcase page was rasterized through both PDFium and MuPDF: two dividers side by side, each
spanning one column's width at that column's own left edge, each note at its own column's foot, and
column 1's prose stopping above its own strip. The paint test asserts the same thing as a single
ordered log - two `FillRect`s at two distinct X positions, each with its own bodies drawn between its
own `PushClip` and `PopClip` - because a per-area count cannot tell "two areas" apart from "one area
drawn twice".

## Deliberately left

`float: inline-footnote` is not implemented and is not a gap - it is a PrinceXML extension absent
from every css-gcpm-3 revision, and its actual meaning (marker inside the body) is already PeachPDF's
only behaviour. See `.claude/accepted-gaps/footnote-inline-footnote-is-not-a-css-feature.md`, which
replaces a note that had the premise wrong. `float-reference` outside a footnote (#1269) and the
bounded column convergence (#1270) have their own gap files.
