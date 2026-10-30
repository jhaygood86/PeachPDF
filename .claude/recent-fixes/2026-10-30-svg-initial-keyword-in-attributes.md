# SVG `initial` in an attribute or `style` attribute

**Defect.** `ApplyCommon` reads each common property with "null or `inherit` or rejected by `TrySet` means the inherited value". `TrySet` and the keyword tables (`Map.WritingModes`...) reject a CSS-wide keyword, so
`initial` written as a presentation attribute or in a `style` attribute landed in the third branch and kept the parent's value. A stylesheet rule did not: `SvgCssDomNodes.GetMatchedDeclarations` swaps `initial` for the
registry's initial value before the builder sees it. This is the sibling of the `visibility` fix (see [the visibility entry](2026-10-30-svg-visibility-initial-keyword.md)), which found the same shape for one property.

**Fix.** `DeclaredOrInitial(node, name)` in `SvgTreeBuilder` reads the cascaded text and, for `initial`, returns `InitialValueOf(name)` (the registry's value, else the `InitialValuesOutsideTheRegistry` table that
the animation path already used). `ApplyCommon` reads fill, stroke, the stroke properties, fill-rule, the two opacities, `visibility`, `direction`, `writing-mode` and `text-orientation` through it, and
`ComputeFontContext` reads `letter-spacing`/`word-spacing` through it. One lookup serves the three forms and the animation path. The table gained `direction`, `writing-mode` and `text-orientation`, because the
registry cannot bind them (they live on the text element, not on `SvgElement`), and `SvgCssDomNodes` now asks the same lookup, so a rule's `initial` resolves for those five text properties too.

**Found by running it.** The issue said "paint properties". A 15-property x 3-form matrix (`SvgInitialKeywordTests`, one case per property and form so a failure names the cell) showed 60 of its 180 cases failing before the
fix: the eleven paint/stroke properties in the attribute and `style` forms, and `writing-mode`/`text-orientation` in **all three** forms, rule included. `direction` passed by accident as an attribute (`initial` is not
`rtl`, so it fell to `ltr`) and failed in a rule; `direction="bogus"` gave `ltr` rather than the parent's value, now fixed alongside. Chrome's computed value was measured for every cell first (initial in all
three forms); the spacings were added to the matrix afterwards and measured the same way.

**Not a regression.** Routing the rule path through the wider lookup could have changed `display`, `transform` and the geometry properties, whose rule `initial` used to be "no value". Reverting only that one
line fails exactly the five text properties' rule cells; the `transform`/`display`/rect/circle geometry tests pass either way (Chrome: `none`, `inline`, `0`), so they are pins, not fixes.

**Trap.** The initial value of a property that is *inherited* only differs from "inherited" inside a parent that sets something, which is why every case puts a non-initial value on the parent.

**Also.** A stylesheet `font-size: initial` must stay a no-op until `font-size` is measured: the animation path's resting size (11px) is not the CSS `medium` (16px), and a first version of this change
let the shared lookup return it, which turned `em` into 11px for the whole SVG. The lookup a declared `initial` reads (`InitialValueOf`) therefore has no `font-size`; only the animation's
(`AnimatedInitialValue`) adds it back.

**Not done.** The other inherited font properties read in `ComputeFontContext` (`font-family`, `font-weight`, `font-style`, `font-stretch`, `font-variant-*`, `-webkit-text-stroke-*`, `font-palette`...) use the same
"inherit or absent" idiom, and have no initial value in the lookup; `initial` for them in an attribute or `style` attribute is unmeasured and presumably still inherited. `font-size` has an initial value in the
lookup but is not read through `DeclaredOrInitial`.

**Evidence.** Chrome's computed value for `initial` measured for all 17 properties in all three forms (the spacings are `normal`/`0px`, which is zero); 204 matrix cases and 4 stylesheet pins; 16,790 engine tests and 122 generator tests pass; 0 build warnings.
