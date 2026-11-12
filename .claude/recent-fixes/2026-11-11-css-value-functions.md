# CSS value functions: math functions, env(), sibling-index/count, contrast-color(), calc-size(), :dir()

Issue #2271 asked which WebKit-shipped value functions PeachBrackets lacked. Findings: `light-dark()` already worked;
`:dir()` *parsed but was listed in `UnmatchableSelectors.FunctionalPseudoClasses` and never matched* (now matches, in
`CssData.DoesSelectorMatch`, from the `dir` attribute up the ancestors; `dir="auto"`/`<bdi>` are already rewritten to ltr/rtl
by `DomParser.ResolveAutoDirectionality`). The rest were missing and are implemented.

## What carries the weight

- **One numeric definition.** `CalcMath.Apply` is the single place each calc-family function's arithmetic lives; the
  folder (`CalcTypeChecker.FoldNumber`), the angle folder, the pixel folder in `CalcSerializer` and the layout evaluator all
  call it. `IsCalcFamily` is `CalcMath.Canonical(name) is not null`, so the ~15 call sites that gate on it pick the new
  functions up with no change. Type rules are in `CalcTypeChecker.CheckCall`; `round(A)` with no step is Number-only.
- **A trap found by running it:** `Combine` unions Length|Angle flags, so `mod(1px, 2deg)` type-checked. `CheckCall` now rejects
  an angle/time/resolution mixed with anything else (this also tightens `min()`/`max()`, correctly).
- **`env()`, `sibling-index()`, `sibling-count()` are text substitutions** in `CssVarResolver.Substitute`, beside `var()`.
  Every Layer A site that keyed on `var(` (`Property.TrySetValue`, shorthand splitting, `DomParser` deferral...) now asks
  `SubstitutionFunctions` instead, so the declaration is deferred until the element is in hand.
- **Sibling style sharing is the trap for `sibling-index()`.** Siblings with the same tag and attributes adopt the first
  one's computed style (`DomParser` `SiblingStyles`), which would give every sibling the first's index. `CssBox.UsesSiblingPosition`
  is set when a deferred value uses either function and stops the style from being recorded for sharing. The test uses
  identical siblings on purpose. (A `<style>` element is itself a sibling and counts toward the index, as in a browser.)
- **`calc-size()`** follows the `stretch` precedent: kept as written by Layer A (lenient `Any` converter, like `color-mix()`),
  rewritten in layout into a plain `calc()` so nothing downstream learns about it. Inline-axis keyword bases are parked and
  measured in `GetBoxWidth` (`ResolveCalcSizeWidthsAsync`), with the declared sizes neutralized while measuring `auto` to avoid
  recursion. Block-axis keyword bases are the accepted gap (see `.claude/accepted-gaps/calc-size-intrinsic-keyword-on-block-size.md`).
- **`contrast-color()`** picks white or black by WCAG contrast; `RelativeLuminance` moved into `ColorSpaceMath` and
  `BorderBevelColors` now uses it. A hex operand inside a function tokenizes as `Hash` in the real pipeline but as a valid
  `Color` token from `CssValueParser.GetCssTokens`, which `ToColor` skips - so hex is tested through a paint test, not the
  token helper.

## Evidence

`CssValueFunctionsTests` (Engine.Tests) and `ValueFunctionPaintTests` (PeachBrackets.Tests); the `css_value_functions`
showcase rasterized through PDFium and MuPDF agrees.
