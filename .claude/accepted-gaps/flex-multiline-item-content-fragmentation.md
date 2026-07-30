# A flex item's own content only fragments for a single-line row container

`CssLayoutEngineFlex.CommitItemContent` (Phase 9c) re-lays-out each item of a line with the
fragmentainer genuinely attached, once the line's `Location` is final — the flex analogue of
`TableRowCursor`/`TableBreakToken`. It bails out (returns without committing anything, leaving the
translate-only path from before this mechanism existed) unless the container is `flex-direction: row`
or `row-reverse`, is block-level (not `inline-flex`), and has **exactly one** line.

Three shapes are therefore still translated rather than fragmented, and a nested container tall
enough to overflow one fragmentainer inside one of these still loses content the way
[issue #430](https://github.com/jhaygood86/PeachPDF/issues/430) originally described:

1. **`flex-wrap: wrap`/`wrap-reverse` with more than one line.** `FlexBreakToken` carries no line
   index — every item of the (one) line it knows belongs to either `UnfinishedItems` or
   `FinishedItems`, with no "line not yet entered" state. A second line that itself needs to resume
   mid-content needs that state added, composed with `RelocateLinesAcrossFragmentainers`'s existing
   decision of which line lands in which fragmentainer.
2. **`flex-direction: column`/`column-reverse`.** A column-direction line's items are a *sequential*
   flow along the block axis, not css-break-3 §2.1 parallel flows the way a row's items are (the
   spec's own example of a parallel flow is "the contents of each flex item in a flex layout row").
   The row-shaped mechanism here is the wrong shape for it — see
   [flex-column-container-has-no-break-points-between-items.md](flex-column-container-has-no-break-points-between-items.md)
   for the related (and also still-open) gap in this same direction.
3. **Grid items**, entirely — `CssLayoutEngineGrid` was not touched. Grid's placement model (track
   sizing, row/column-spanning items) is different enough that porting `FlexSetup`/`FlexBreakToken`
   without dedicated design would be exactly the hand-waving this repo's own conventions ask against.

Filed as [issue #526](https://github.com/jhaygood86/PeachPDF/issues/526), which carries a starting
design for each of the three.
