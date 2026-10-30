# SVG `visibility: initial` resolves to `visible`

**Defect.** Two separate paths resolved `visibility: initial` to the inherited value, and `initial` is `visible`:
- the attribute and inline-`style` forms, where the `visibility` chain in `SvgTreeBuilder` mapped `visible` to true, `hidden`/`collapse` to false and *everything else* to the inherited value;
- the stylesheet form, where `SvgCssDomNodes.GetMatchedDeclarations` resolves `initial` through `SvgPropertyRegistry.GetInitialValue`, and `visibility` had no `svg` binding in `css-properties.json`, so the lookup gave null
  and a null-present declaration means inherited.

The animated `initial` was already right: the animation path resolves CSS-wide keywords through `AnimatedInitialValue`, which had a hand-kept `visibility` row in `InitialValuesOutsideTheRegistry`.

**Fix.** `visibility` now has an `svg` binding in `css-properties.json` (`Visible`, `bool`, `invalidBehavior: inherit`, `customValidator`/`customSetter` over the shared `SvgValueParsers.TryParseVisibility`, which maps
`collapse` to hidden). That one entry is the single source of the initial value for the stylesheet path, the animation path and the attribute path, so the `InitialValuesOutsideTheRegistry` row and any per-property
special case are gone. `ApplyCommon` follows the other common properties (null or `inherit` or an invalid value gives the inherited value) with one addition: `initial` is replaced by the registry's initial value before
`TrySet`, because `TrySet` rejects a CSS-wide keyword. `inherit`, `unset` and `revert` keep resolving to the inherited value, which is correct for an inherited property.

An earlier version of this fix special-cased `visibility` in `SvgCssDomNodes` (`GetInitialValue(...) ?? "visible"`). It behaved the same and was replaced by the registry binding on review: a one-property branch beside a
lookup that is meant to be the single source of SVG initial values.

**Trap.** Fixing the attribute path alone looks complete: a test of the attribute and `style` forms passes while a class rule still hides the element. Only a test that puts the keyword *inside a hidden ancestor* shows the
bug at all (at the top of the tree "inherited" and `visible` are the same value). `SvgVisibilityTests`' `ACssWideKeyword_InsideAHiddenGroup_ResolvesAsTheSpecSays` does that for all four keywords in all three forms; breaking
the `initial` replacement in `ApplyCommon` fails exactly its `initial` case. `SvgPropertyRegistryEquivalenceTests` pins `TrySet` against `SvgValueParsers.TryParseVisibility`.

**Not done.** The same "unrecognised value means inherited" shape holds for `fill: initial` written as an attribute or in a `style` attribute (Chrome black, PeachPDF the inherited colour; measured for `fill` only) and is
tracked in #1982. `InitialValuesOutsideTheRegistry` also holds the inherited `letter-spacing` and `word-spacing`, which probably resolve a stylesheet `initial` to the parent's value the same way; not measured.

**Evidence.** Chrome across 150 cells (10 keywords x attribute/style/rule x 5 placements: shape, `<tspan>`, `<use>` twice, nested `<svg>`), rasterized through PDFium and MuPDF with identical results: every `initial`
cell matches Chrome after the fix. 16,582 engine tests and 122 generator tests pass; the solution rebuilds with 0 warnings.
