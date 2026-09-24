# A float, or inline-block, taller than a page shows all of its lines (#1201, #1332)

**Symptom, measured:** on 160pt page bands with 20pt lines, an inline float of twenty lines
(`Before <span style="float:left">…</span> after`) painted eight of them (`G1`-`G8`) and never the rest; an
inline-block holding twenty block lines did the same; a `float: bottom` of twelve lines (240pt) painted
`F5`-`F12` because its top was placed 80pt above the page, and `F1`-`F4` were on no page. A block-level
`float: left` of the same height was already fine (`H1`-`H20` across three pages), which is what located the
defect: it is specific to boxes an *inline flow* places itself.

## Inline flow: lay the content out unbroken

`CssLayoutEngine.FlowFloatChild` and `FlowAtomicBlockContentChild` called `LayoutContentAtItsAssignedPosition`
with the fragmentainer live. A box too tall for what is left of it stops with a `PendingBreakToken`, and nothing
up the inline flow (`FlowBox`'s per-word ordinal resumption) has a slot to resume "this one non-word child's own
content", so everything after the break vanished. The fix is `LayoutContentUnbroken`: detach the fragmentainer
and set `SuppressWordPageBreaks` around that call, which is what `CssBox.LayoutContents` already does for a
monolithic box. The box's geometry then runs on past the page's foot and each page's fragment shows its slice
(`content-that-cannot-be-broken-is-displaced-per-band-never-resized`).

**A comment in `FlowAtomicBlockContentChild` said not to do this**: an earlier version that detached produced a
correct box tree "whose PDF content stream carried zero text objects". Measured now, detaching *around the
content layout only* paints every line (checked by painting every page through the fragment tree, and the
inline-block, float and page-float shapes all pass), so whatever that version detached around, it was more than
this. The comment is gone; the shapes are pinned by `TallFloatIntegrationTests`.

## Page float taller than the band: top of the page, no reservation

`ResolvePageFloatsForThisAttempt` reserved the float's height at its edge and, for `float: bottom`, placed its
top at `bottomEdge - height`, above the page when the float is taller than the band. A float taller than
`bandHeight - footnoteHeight` is now placed at the top of its page and neither stacked nor reserved for: an edge
strip as tall as the band leaves the page no room for anything else, and a reservation that large stalls the
flow (`CssLayoutEngineColumns.FillColumns` documents the same shape for a column-scoped note area).

**Cost, not hidden:** with no reservation, in-flow content on the float's page and the pages it runs onto can
overlap it. Reserving the remainder on each following page (`TopFloatAreaHeightsBySlot` for slots k+1...) is the
better answer and was not done here; it is what to reach for if that overlap matters.

## Not done

Real float fragmentation (#317): the float is sliced, not broken at its own class-A/B break points, so it does not
continue *beside* the wrapped text on the next page. That needs `InlineBreakToken` to carry a per-child
continuation record.
