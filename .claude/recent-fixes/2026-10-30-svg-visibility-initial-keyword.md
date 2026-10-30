# SVG `visibility: initial` resolves to `visible`

**Defect.** In `SvgTreeBuilder` the `visibility` chain mapped `visible` to true, `hidden`/`collapse` to false and *everything else* to the inherited value. For `initial` that is wrong: it is
`visible`. The keyword is rare, so nothing caught it until the animation work made the same builder read `visibility` from keyframes (the animated `initial` was already right, because the animation
path resolves CSS-wide keywords itself; only the static declaration was wrong).

**Fix.** `initial` joins `visible` in the one place the chain is written. `inherit`, `unset` and `revert` keep falling through to the inherited value, which is correct for an inherited property
(`revert` rolls back to the user-agent value, which for `visibility` is the inherited one).

**Trap.** Only a test that puts the keyword *inside a hidden ancestor* shows the bug: at the top of the tree "inherited" and `visible` are the same value. `SvgVisibilityTests`'
`ACssWideKeyword_InsideAHiddenGroup_ResolvesAsTheSpecSays` does that for all four keywords, as an attribute and as a `style`. With the fix reverted exactly the `initial` case fails.

**Evidence.** Chrome (computed `visibility` of the rect: `initial` visible; `inherit`/`unset`/`revert` hidden); `SvgVisibilityTests` 11/11.
