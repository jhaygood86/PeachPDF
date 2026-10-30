# SVG `visibility: initial` resolves to `visible`

**Defect.** Two separate paths resolved `visibility: initial` to the inherited value, and `initial` is `visible`:
- the attribute and inline-`style` forms, where the `visibility` chain in `SvgTreeBuilder` mapped `visible` to true, `hidden`/`collapse` to false and *everything else* to the inherited value;
- the stylesheet form, where `SvgCssDomNodes.GetMatchedDeclarations` resolves `initial` through `SvgPropertyRegistry.GetInitialValue`, and `visibility` has no `svg` binding in `css-properties.json`, so the lookup gives
  null and a null-present declaration means inherited.

The animated `initial` was already right: the animation path resolves CSS-wide keywords through `InitialValuesOutsideTheRegistry`.

**Fix.** `initial` joins `visible` in the `SvgTreeBuilder` chain, and the stylesheet branch falls back to `visible` for `visibility` only. Routing it through `InitialValuesOutsideTheRegistry` for every property was
not done: it would also change what a stylesheet `initial` means for `display`, `transform` and the geometry properties, which needs its own measurement. `inherit`, `unset` and `revert` keep resolving to the
inherited value, which is correct for an inherited property.

**Trap.** Fixing the first path alone looks complete: a test of the attribute and `style` forms passes while a class rule still hides the element. Only a test that puts the keyword *inside a hidden ancestor* shows the
bug at all (at the top of the tree "inherited" and `visible` are the same value). `SvgVisibilityTests`' `ACssWideKeyword_InsideAHiddenGroup_ResolvesAsTheSpecSays` does that for all four keywords in all three forms;
with each half of the fix reverted, exactly the `initial` case of the forms that half serves fails.

**Not done.** The same "unrecognised value means inherited" shape holds for `fill: initial` written as an attribute or in a `style` attribute (Chrome black, PeachPDF the inherited colour; measured for `fill` only) and is
tracked in #1982.

**Evidence.** Chrome across 150 cells (10 keywords x attribute/style/rule x 5 placements: shape, `<tspan>`, `<use>` twice, nested `<svg>`), rasterized through PDFium and MuPDF with identical results: the
attribute and `style` forms agreed after the first half of the fix, the stylesheet form after the second. `SvgVisibilityTests` 11/11.
