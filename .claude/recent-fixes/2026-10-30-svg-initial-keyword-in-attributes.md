# SVG `initial` in an attribute or `style` attribute

**Defect.** `ApplyCommon` reads each common property with "null or `inherit` or rejected by `TrySet` means the inherited value". `TrySet` and the keyword tables (`Map.WritingModes`...) reject a CSS-wide keyword, so
`initial` written as a presentation attribute or in a `style` attribute landed in the third branch and kept the parent's value. A stylesheet rule did not: `SvgCssDomNodes.GetMatchedDeclarations` swaps `initial` for the
registry's initial value before the builder sees it. This is the sibling of the `visibility` fix (see [the visibility entry](2026-10-30-svg-visibility-initial-keyword.md)), which found the same shape for one property.

**Fix.** `DeclaredOrInitial(node, name)` in `SvgTreeBuilder` reads the cascaded text and, for `initial`, returns `SvgPropertyRegistry.GetInitialValue(name)`. `ApplyCommon` reads fill, stroke, the stroke properties,
fill-rule, the two opacities, `visibility`, `direction`, `writing-mode` and `text-orientation` through it, and `ComputeFontContext` reads `letter-spacing`/`word-spacing` through it. The registry is the **only** source
of an SVG initial value: the hand-kept `InitialValuesOutsideTheRegistry` table the animation path used to have is gone (see
[the invariant](../invariants/svg-a-property-the-builder-reads-needs-an-svg-registry-entry.md)).

To make that true, every property the builder reads through the cascade has an `svg` entry in `css-properties.json`. The ones `SvgElement` does not store are **carrier-only** (`propertyPath: null`, `applyIn: "manual"`,
as `direction` already was): `transform`, `display`, `width`, `height`, `writing-mode`, `text-orientation`, `letter-spacing` and `word-spacing` (existing entries that gained the block) and `x`, `y`, `cx`, `cy`,
`r`, `rx`, `ry`, `stop-color`, `stop-opacity` (new entries; their CSS-OM registration already existed, see [the geometry entry](2026-10-19-svg-geometry-css-properties.md)). Two generator changes made that
possible: the SVG `InitialValues` dictionary now includes manual entries (it used to skip them, so `direction`'s own value never reached `GetInitialValue`), and a manual entry no longer needs a `propertyPath`,
`customSetter` or `inheritedFrom` (PPG005 still fires for a common one). `all` still excludes `direction`, as the spec says, through `AllShorthandTests.IsReviewedExclusion`.

**The spacings' `normal`.** The registry says `normal`, the old table said `0`. A declared `initial` reads `normal` fine (`ResolveSpacingLength` returns 0), but the animation's interpolator only mixes a length with a
length, so `AnimatedInitialValue` maps the two spacings to `0`, and keeps its `font-size` resting size (11px). A declared `initial` for `font-size` is deliberately **not** in the lookup (see below).

**Found by running it.** The issue said "paint properties". A 15-property x 3-form matrix (`SvgInitialKeywordTests`, one case per property and form so a failure names the cell) showed 60 of its 180 cases failing before the
fix: the eleven paint/stroke properties in the attribute and `style` forms, and `writing-mode`/`text-orientation` in **all three** forms, rule included. `direction` passed by accident as an attribute (`initial` is not
`rtl`, so it fell to `ltr`) and failed in a rule; `direction="bogus"` gave `ltr` rather than the parent's value, now fixed alongside. Chrome's computed value was measured for every cell first (initial in all
three forms); the spacings were added to the matrix afterwards and measured the same way.

**Trap.** The initial value of a property that is *inherited* only differs from "inherited" inside a parent that sets something, which is why every case puts a non-initial value on the parent. And a `<style>` inside an
inline `<svg>` is global to the HTML document: a Chrome comparison that puts one rule per `<svg>` under a shared class sees every rule on every element. Give each case its own class.

**`font-size`.** A first version of the shared lookup returned the animation's resting text size for `font-size`, which turned a stylesheet `font-size: initial` into `em` = 11px for the whole SVG. `font-size` has no
SVG registry entry, so a declared `initial` stays a no-op; `font-size: initial` and `medium` keeping the parent's size (Chrome: 16px) is #1986.

**Judgement call.** `text-orientation="initial"` as a presentation **attribute** is now `mixed`. Chrome ignores the attribute (SVG 2 does not list `text-orientation` as a presentation attribute) so it keeps the
parent's value; PeachPDF has always read `text-orientation` as an attribute (`text-orientation="upright"` works), so `initial` follows the same reading rather than being the one value that is ignored.

**Not done.** The other inherited font properties read in `ComputeFontContext` (`font-family`, `font-weight`, `font-style`, `font-stretch`, `font-variant-*`, `-webkit-text-stroke-*`, `font-palette`...) use the same
"inherit or absent" idiom and have no entry in the registry; `initial` for them in an attribute or `style` attribute is unmeasured and presumably still inherited (named in #1986).

**Evidence.** Chrome's computed value for `initial` measured for all 17 properties in all three forms (the spacings are `normal`/`0px`, which is zero); 204 matrix cases and the stylesheet pins; reverting the generator
change fails 30 of them; the solution rebuilds with 0 warnings. The maintainer's own trial merge of the previous revision showed byte-identical PDFs for 225 of 228 showcases (the other three pixel-identical) and 537 CLI fixtures.
