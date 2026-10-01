# Right floats in the same block collide, and a dropped float clears the margin box (#1522)

The issue read the gap as "a second `float: right` that does not fit beside the first overlaps it". Running it showed
the gap was wider: **no two right floats in one containing block ever collided**, fitting or not. Two 100pt right
floats in 260pt were both placed at the right edge, on top of each other (`fa`/`fb` both at X=180; Chrome puts `fb`
at X=80).

**Cause.** `DomUtils.IsFloatIntersecting`'s `Floating.Right` arm asked whether the *target* float lies entirely past
this float's right edge (`targetLeft > Right + marginRight`). That was written for a float in an *ancestor* that sticks
out past a narrower nested block's right edge (the existing `FloatRight_InNarrowerNestedBlock_...` tests); a float
in the same containing block can never satisfy it. The old test comment said so ("neither is reachable when ... the
same containing block"), and the right-float branch of `FloatBoxRight` therefore never ran for siblings.

**Fix.** A second `Floating.Right` arm: an earlier float whose margin box shares any of the width this float would take
(`targetLeft < Right + marginRight && targetRight > FloatRightStartX - marginLeft`) is a blocker (CSS 2.1 §9.5.1
rules 1-3), with the same multicol "another column is not a blocker" exclusion as the first arm. `FloatBoxRight` then
makes the margin edges meet (`Right = blocker.X - blocker.marginLeft - marginRight`; the old
`blocker.X + max(0, -marginRight)` ignored both positive margins) and, on a drop, goes below the blocker's **margin**
box and adds its own `margin-top` (rule 8). The drop test compares against the margin-adjusted left, so it agrees
with the new intersection test; otherwise a blocker could be found again and again.

**Test that had to change.** `FloatRight_DroppedByANarrowedScanBoundary_RederivesAtTheLandingPage` reached the drop
branch with a negative-`margin-left` "ghost" float, because that was the only way to make the old arm fire (its own
comment says so). With sibling right floats colliding, a plain tall float does the same job, so the hack is gone.

**Not done.** `FloatBoxLeft` still takes `MaxBottom` from the blocker's border-box bottom. Its table is correct for
the cases the issue lists, but a left float after a left float with `margin-bottom` goes up to that margin short.
It was left alone to keep this change to the right-float path; it is a one-line mirror of what is done here.

Evidence: `FloatPlacementTests` (the issue's four rows, plus the side-by-side case that fails on `main` too); full
net8.0 suite, 14832 tests, passes.
