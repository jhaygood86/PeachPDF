# A float placed in inline flow takes its `margin-top` (#1786)

**Symptom.** A float that is the first box of its block (so it is flowed with the inline run rather than as a block
child) ignored its `margin-top`; a second left float with a larger negative `margin-top` was placed over the first
instead of beside it. After a paragraph the margin was applied, because that float goes through block flow.

**Cause.** `CssLayoutEngine.PlaceFloatAtLineTop` set the float's `Location.Y` to the line top with no margin, while the
block-flow path (`CollapsedMarginBefore`, which never collapses a float's margin) already gave a border-box top of
margin edge + `margin-top`. `FloatBoxRight` papered over it by reading the handed-over position as the margin edge
(`OuterTop = startY`); `FloatBoxLeft` read it as the border-box top.

**Fix.** `PlaceFloatAtLineTop` adds `ActualMarginTop` (css2 §9.5.1 rule 6 puts the margin edge at the line top,
§8.3.1 never collapses it), and `FloatBoxRight` drops its `OuterTop` override, so `CssFloatCoordinates.OuterTop`
is `Top - MarginTop` for a negative margin for both sides.

**Evidence.** `FirstChildFloat_AppliesItsNegativeMarginTop_SoASecondFloatSitsBesideIt` (left and right); the float
and multicol suites and the full `PeachPDF.Tests`/`PeachBrackets.Engine.Tests` suites pass unchanged.
