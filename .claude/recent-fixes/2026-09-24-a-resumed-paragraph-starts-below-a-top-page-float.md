# A paragraph continuing onto a page with a `float: top` starts below the strip (#1273)

**Symptom:** a paragraph that began on an earlier page and was still flowing when it crossed onto a page
that reserves room at its head for a `float: top` drew its first lines inside the reserved strip, under
the float. Measured with 20pt lines on 160pt pages and a 50pt float anchored on page 1: the continued
lines sat at Y = 180, 200 and 220 with the strip ending at 230.

**Cause:** the block-level floor in `CssBox.ResolveBlockChildOffset` keeps a *box* off the strip, but a
resumed inline flow is not placed by it. `CssLayoutEngine.CreateLineBoxes` starts a resumed pass at
`FragmentainerContext.ResumeContentTop` - the fragmentainer's content edge - which knows about a repeated
header's `ResumeContentInset` and nothing about the band-start reservation.

**Fix:** the same floor, taken the same way: the resumed flow starts at the larger of `ResumeContentTop` (plus
any cloned decorations) and `BandTop + BandStartInsetOf(SlotIndex)`. Only the first line needs it -
`OpenNextLine` opens every later line from `MaxBottom`. It reads zero for a document with no `float: top`, so
nothing else changes.

**Deliberately not done:**
- `ResumeContentTop` itself is unchanged. Its other readers (`CssBox` ~5205 for a resumed column,
  `CssLayoutEngineColumns` for a resumed multi-column container) are answering a different question, and the
  strip is a per-fragmentainer band inset, not a property of every resume.
- A repeated table header and a top float on the same page both claim the page's top edge. The floor takes the
  larger of the two, as the block-level one does against the header; the spec does not say how they compose.

**Evidence:** `PageFloatIntegrationTests.FloatTop_OnALaterPage_KeepsAParagraphContinuingFromAnEarlierPageBelowTheStrip`
fails without the change (lines at 180/200/220 inside a strip ending at 230) and passes with it; the control
without a float starts its continued lines at the page top.
