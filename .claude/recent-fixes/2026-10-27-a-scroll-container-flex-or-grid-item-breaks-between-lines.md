# An auto-height scroll container that is a flex or grid item breaks between its lines (#1528, part)

Before: every flex or grid item that was a scroll container counted as monolithic, so a tall `overflow: auto` panel in
a flex row was moved whole or cut by its clip at the page edge (2 of 30 lines of the issue's panel were outside the
content area). `MonolithicContent.IsLaidOutByAnEngineThatCannotContinueIt` returned true for any flex or grid item.

Found by running it: removing that clause alone changed nothing. The commit pass pins the item's height
(`ItemContentCommit.CommitLayout`), and `HasConstrainedLogicalHeight` then read the pinned height as an author-declared
definite one, so the item was monolithic through a second door. The pre-pin height is already kept
(`HeightBeforeItemPin`); the classifier now asks that (`HasDefiniteAuthoredHeight`) and a flex or grid item no longer
needs a clause of its own. A percentage height falls back to the old answer.

Measured with a sweep of the lead paragraph length (0 to 100 words, step 2) before a 30-line panel plus a paragraph,
with system Arial on a 300pt x 200pt page: `overflow: auto` flex and grid items lose nothing at any length (was 51 of 51
positions losing 2 words); `overflow: hidden` ones lose one line at 8 of 51 positions (84 to 98 words), see the gap file.

Not done: the `hidden` residual (the clip, taken from the pinned height, ends 2.2pt above the last line) and the
multi-column case. Growing the box to its lines at the end of `CommitLayout` set the bottom but the clip still read the
pinned one at emission; where it is reset was not found. Both are in
`.claude/accepted-gaps/auto-height-scroll-container-in-a-flex-grid-item-or-a-column-stays-unbreakable.md`.

Evidence: `TallAutoHeightScrollItem_BreaksBetweenLines` (flex and grid, auto and scroll) and the updated
`ScrollContainerInAnEngine_IsMonolithicOnlyThere` fail without the change; Engine.Tests 16521 and PeachPDF.Tests 1499 on
net8.0 (Debug). The old pinned guard `AutoHeightScrollItemStraddlingAPageEdge_DrawsEveryWord` (88-word lead, all six
combinations) still passes.
