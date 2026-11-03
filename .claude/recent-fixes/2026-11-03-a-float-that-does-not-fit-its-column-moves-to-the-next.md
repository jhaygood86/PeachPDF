# A float that does not fit its column moves to the next one (#1341)

**Idea.** In the block child loop (`CssBox.LayoutBlockChildren`), a float child laid out in a column (`HasOwnBand`) whose margin box ends below
the column band, with something above it in the column and no taller than a column, is *deferred*: the loop `continue`s, so the in-flow content
after it keeps filling this column (CSS 2.1 section 9.5.1 rules 1 and 3 move only the float). It is held in `CssBox.DeferredFloats`. The next
column's fill (the same box resumed with its break record) lays those floats out first, with `_resumeTopOverride` set to the column's content
top and `PassRewind.RollBackTo` first so the second layout does not stack on the first. `AddBoxesBeyond` lists them as beyond the column they
were measured in, so that column's fragment does not draw them. When every child was placed but a float is still owed, the loop ends with a
break record that names only the float (`DeferredFloatsOnly`), so a next column exists to hold it.

**Why not break before the float.** The first attempt did that (the existing arm for in-flow children, widened to floats). It worked for a
fixed-height column but moved every in-flow sibling after the float into the next column too, leaving a gap, and the first in-flow child there
took its top from the previous sibling in the earlier column. Keeping the floats on the box rather than on the break token means every
`BlockBreakToken` construction site and `RestatedInTheNextColumn` keep working unchanged.

**Found by running.** A float that has its own break record (it fragments at the band) is not deferred: dropping it lost every word after the
first (`ContainerWhoseResumeBoundaryIsAFloat_DoesNotThrow_AndKeepsEveryWord`). One that still breaks in the next column continues from its
record. State is reset by a fill with no record, so a balance retry starts clean.

**Evidence.** Six new `MulticolFloatIntegrationTests` (fixed-height, in-flow content stays, nested in a block, balanced, fits, taller than a
column); engine and PeachPDF suites green; all 233 showcases rasterize identically.
