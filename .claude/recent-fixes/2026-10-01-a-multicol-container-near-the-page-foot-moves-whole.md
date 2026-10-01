# A multi-column container that starts with less than a line of room moves to the next page (#1532)

A `columns: 2` container starting 8pt above the page foot (less than one 12pt line) drew its paragraph across the foot,
where the PDF clips it: every word was in the paint log and none was visible.

**Cause.** `CssRect.WouldStraddleFragmentainer` has a column arm with a "too tall for any column, so overflow rather
than break to a fresh column for every column there is" exemption (`FitsInBand(Height, ..., columnBand.BandHeight)`).
The column band is the container's *target*, which the page clamps to what is left of it: 8pt. A 12pt line is
"taller than any column", so the break was never taken. The exemption exists to stop a perpetual deferral, which
only applies when a fresh fragmentainer would not help either; a fresh page would.

**Fix.** A column whose band ends where the page does (less the page's foot reservations) records
`FragmentainerContext.FreshPageBandHeight`, the height it would have on a fresh page, and both the word and the line
version of the exemption (`WouldStraddleFragmentainer`, `LineFitsNoFragmentainer`) compare against that. A column
sized by the container itself (`height`, balancing) leaves it null, so a word taller than a deliberately short column
still overflows it, as before.

**Trap.** The first test of this issue (every word in the paint log) passed on `main`, because the words *were*
painted. The visible-band check (`PaintedWords.LayOutAndCollectVisibleAsync`) is what reproduces it; the earlier
`absolute box` note in `2026-09-30-an-absolute-box-breaks-in-passes-...` makes the same observation for #1531/#1532.

Evidence: `MulticolContentLossTests.ContainerStartingTooCloseToThePageFoot_...` (10, 11, 12 and 13 fillers, words and
page count); full net8.0 suite, 14835 tests, passes.
