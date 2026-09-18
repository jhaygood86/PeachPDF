# A multi-line heading kept with a `break-inside:avoid` box no longer renders twice

A `break-inside:avoid` box whose own first child is a multi-line heading — chained to what follows it by
the UA default `h1`–`h6` { `break-after: avoid` } rule — could, at a specific range of positions relative
to a page boundary, have its last line of content rendered on **two consecutive pages** instead of one.
This was issue #1047: the duplicated line was not a rendering artifact of already-correct geometry, but a
genuine second copy of the same content, produced by two independent defects compounding:

- `CssBox`'s keep-with-next restart (pulling a heading forward to keep it with the content below it) left
  the pass's own page cursor pointed at the page it was leaving rather than the one it was restarting the
  heading into, so the heading's re-measurement against the wrong page could force an unnecessary extra
  layout pass.
- Separately, `FragmentEmitter`'s line-to-page assignment had a tie-break intended to rescue a line whose
  nominal page (computed from a negative `line-height`'s upward ink escape) had already been closed out by
  an earlier, unrelated page. That tie-break could also fire for a line that had *already* been correctly
  assigned to a page earlier in the same layout pass, handing the same line a second, conflicting page
  assignment.

Both are fixed. A document with this shape now renders the heading and the content it is kept with once,
on the correct (destination) page, with no duplicate content on the page before it.

A related, lower-severity imprecision is not fixed here and is left for a follow-up: the same
`break-inside:avoid` box's own "does this fit the destination page" check can still overstate how much
room a fresh layout would need when its content was already relocated internally by the keep-with-next
restart above, occasionally causing the box to be shifted as a whole (rather than laid out again cleanly)
and to slightly overflow its destination page's own content area. This does not reproduce the duplicate-
rendering behavior above — every word still lands on exactly one, correct page — and is a narrower version
of the same overall issue, tracked separately.
