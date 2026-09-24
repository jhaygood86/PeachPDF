# A preceding float must not enter an independent box's line layout (#1335)

**Symptom:** a cart link with an SVG background and an absolutely positioned badge could
render beyond the right page edge. Removing the badge appeared to restore the icon, but the
badge was not the root cause. The cart's link rectangle had been shifted by a wide float that
preceded its absolutely positioned parent, then the parent's position was applied as well.

**Cause:** `LeftFloatAt` asks `DomUtils.GetLastLeftIntersectingFloatBox` for preceding floats
while laying out a line. That walk deliberately checks siblings of its starting box, which
is useful for placing floats against other floats. When the starting box itself establishes
an independent formatting context, those siblings are outside the line's formatting context.
The cart's own inline content was therefore laid out against a float in the surrounding row.

**Fix:** skip the left ancestor float lookup when the line's reference box establishes an
independent formatting context. The right lookup already had this boundary in
`FindNarrowestRightFloatBox`. Floats in the line's own `InlineFloats` remain available to
its line layout. The preceding float still affects the surrounding flow and other floats.

**Empirical finding:** in the original MHTML, the cart SVG Form was absent from the visible
page while its badge appeared. Before the fix the cart link fragment sat around X=1065 pt
on a 612 pt page. After the fix the cart Form painted around X=559 pt. A reduced regression
asserts that the link's line rectangle starts at its absolute parent's X and stays on page;
the `positioned_inline` showcase includes an SVG background and positioned badge after a left float.

**Separate visible artifact:** the blue line across that showcase's cart icon is an underline
painted over the full width of its empty inline-block link. It is the existing text-decoration
defect tracked by [#1320](https://github.com/jhaygood86/PeachPDF/issues/1320), not a float or
background-image problem. Keep this distinction in mind when judging the showcase render.

**Scope:** this change does not alter float placement in the containing row or positioning
of the absolute cart. It corrects which floats may influence the cart's internal line layout.

**Evidence:** see the regression test and `positioned_inline` showcase. Full suite,
coverage, build, and showcase render results are recorded in the PR.
