# A broken overflow: hidden flex or grid item is not shrunk below its content (#1528, part)

Before: a flex or grid item with `overflow: hidden` whose content ran over several pages lost its last line at some
positions of the foot (8 of 51 lead-paragraph lengths, 84 to 98 words, with system Arial on a 300pt x 200pt page): the
clip ended 2.2pt above the last line, so the line was cut away.

Cause, found by a hook on `CssBox.Size` that printed the stack when a box was written below a bottom it had been given:
the commit pass pins the item's height to the unbroken measurement, and every break that moves a line to the next page
adds the strip it left at the foot, so the content ends below the pinned bottom. Growing the box to its lines at the end
of `ItemContentCommit.CommitLayout` (the earlier attempt) did set the bottom, but `CssBox.PerformLayoutEpilogue` runs
`ApplyParentHeight` up the ancestors, and each one reached the item again through `ApplyHeight`, which assigns the
explicit used height (`ActualBottom = Location.Y + height`) over the content's bottom.

Fix: `ApplyHeight` takes the table-cell branch (`Math.Max` with the content bottom, never shrink) for a pinned flex or
grid item with an author height of auto that is a breakable scroll container.

Evidence: a sweep over the lead length 0 to 110 (step 1) for flex and grid, `hidden` and `auto`, a 30-line and a
5-line panel: 0 of 111 positions bad in all eight combinations (was 8 of 51 for `hidden`).
`HiddenScrollItemBrokenOverSeveralPages_KeepsItsLastLines` (14 cases, pinned font) fails at the flex/26 case without
the change; Engine.Tests 16535 and PeachPDF.Tests 1499 on net8.0 (Debug).
