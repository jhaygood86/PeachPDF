# A right float with a negative `margin-right` in a multi-column container no longer hangs layout (#1534)

**Symptom.** Layout never returned for a `float: right` with a negative `margin-right` in a
`columns: 2` container when an earlier tall right float lay beside the container.

**Cause.** Two things. `DomUtils.IsFloatIntersecting`'s right branch found the earlier float (in the
*second column's* area, past the first column's right edge) and `FloatBoxRight` took its left edge as the
new `Right`, widening the float's range out of its own column. And `Right = blocker.X` left
`Right + marginRight` (the margin edge) still past the blocker's left when `marginRight < 0`, so the same
blocker was found again with identical state, forever.

**Fix.** `CssFloatCoordinates` carries `ContainingRight` (the float's containing-block right edge) and
`MulticolRight` (the nearest enclosing multicol container's right edge); a blocker starting in between
lies in another column and is skipped. `FloatBoxRight` now sets `Right = blocker.X + max(0, -marginRight)` so
the margin edges meet and the loop always terminates; zero/positive margins are unchanged.

**Not done / traps.** A blocker past the *nested block's* right edge but outside any multicol container must
still count (`FloatRight_InNarrowerNestedBlock_*` tests depend on it), so the skip is multicol-specific, not a
general "ignore floats beyond the containing block" rule. Two right floats in one plain container are not
stacked by the right-float scan today; untouched.

**Evidence.** `RightFloat_InAColumn_IgnoresAnEarlierRightFloatInAnotherColumn` (hung before the fix).
