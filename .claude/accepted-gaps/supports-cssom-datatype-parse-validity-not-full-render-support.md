# A handful of `@supports`'s `cssDataType: "cssom"` properties are multi-token/complex grammars, not individually re-derived

Tracking issue: [#601](https://github.com/jhaygood86/PeachPDF/issues/601).

`css-properties.json`'s `cssDataType: "cssom"` is the fallback for a property with no dedicated grammar
modeled in this schema (see `CLAUDE.md`'s generator section). Its HTML validator delegates to the real
CSS-OM property for the same name (`PropertyFactory.Instance.Create(name)` + `StylesheetParser.Default
.ParseValue` + `TrySetValue`) rather than accepting unconditionally, so it only accepts what that
property's own grammar accepts. On its own that is only a proxy for "PeachPDF's renderer genuinely
supports this value" — the CSS-OM's grammar can, in principle, accept syntax that layout, fragmentation,
or paint then silently ignores or degrades rather than actually honoring.

A full pass over every property that used to be `"cssom"` (previously the untyped `"any"`) replaced ~66
of them with a real, verified grammar: a `"keyword"` list cross-checked against actual branches in
`CssLayoutEngine*.cs`/`FragmentPainter*.cs`/`MonolithicContent.cs`/`Fonts/*Resolver.cs` (not just the CSS
spec's keyword set), or `"length"`/`"number"`/`"integer"`/`"color"` for properties whose real grammar is
simple and single-token. Two properties needed more than a data type: `transform` and
`break-before`/`break-after`/`break-inside` are cases where real cascade dispatch must stay as permissive
as before (a `transform` value with one unimplemented function, or a `break-before: region`, is still
syntactically valid and its implemented parts still apply — rejecting the whole declaration would be a
real behavior regression, not a fix), while `@supports` genuinely is stricter (does the renderer honor
*this specific value*, not just parse it). `PropertyEntry.SupportsCssDataTypes`/`supportsDataType` in the
schema exists for exactly this split: `cssDataType` keeps gating real dispatch; `supportsDataType`, when
present, is a separate, stricter grammar `Supports_*`/`SupportsDeclaration` uses instead. `transform`'s
`supportsDataType: "transform"` (`CssValueParser.IsValidTransformValue`) checks against the exact set of
functions `BuildFunctionMatrix` implements at paint time — `perspective()` parses as valid CSS but paint
silently drops it, so `@supports (transform: perspective(500px))` correctly reports unsupported even
though the declaration itself is still accepted and stored by real dispatch. `break-before`/`break-after`'s
`supportsDataType` excludes `region`/`avoid-region` (parsed and stored, per `CssUtilsTests
.Cascade_BreakBefore_StoresOnlySpecValues`, but inert — no `FragmentationContext.Region` exists).

**Still `"cssom"`, deliberately.** ~48 properties remain `"cssom"` — most because their real grammar is a
multi-token or otherwise complex production a flat `"keyword"`/`"length"` clause can't express without
its own bespoke parser (comma-separated multi-layer values for every `background-*` longhand and
`object-position`; 1-2 value forms for the four `border-*-radius` longhands and `border-spacing`; full
gradient/shape/shorthand-like grammars for `background-image`/`list-style-image`/`box-shadow`/`clip-path`;
CSS Grid's line-based placement and template grammars for every `grid-*` property; multi-keyword
combinations for the `font-variant-*` longhands and `font-feature-settings`). For these, delegating to
Layer A's own CSS-OM property is accurate rather than merely convenient: this repo's CSS-OM was written
for this renderer specifically (not a spec-complete generic implementation), and `docs/html-css-support.md`
independently documents comprehensive, verified support for each of these features, so the CSS-OM's
grammar is a reliable proxy for what paint/layout/fragmentation actually do with the value — unlike
`transform`, there is no known case here of a value that parses but is then silently no-op'd.

Two known, narrower residual gaps of that same "parses, but not quite what actually renders" shape,
identified but not fixed in this pass because both are multi-token (space-separated keyword lists), which
the schema's `"keyword"` data type can't express without further generator work: `text-decoration-line`'s
`blink` keyword (`FragmentPainter.Decorations.cs` has no case for it — silently draws nothing) and
`text-decoration-style`'s `double`/`wavy` (recognized but rendered identically to `solid`, no true
double-line or wave rendering). Both remain `"cssom"`, so `@supports` currently reports them as supported
despite the paint-time gap. `font-style`'s `oblique <angle>` is *not* in this category — it is genuinely
implemented (`FontObliqueAngleResolver`) and was independently verified against `docs/html-css-support.md`.

Auditing the remaining `"cssom"` properties in the same test-driven way (JSON grammar → build → run the
full suite → fix any real dispatch regression the way `overflow`'s `auto`/`scroll` and `break-before`'s
`region` were caught here) is future work, not a blocking prerequisite — none of them are known to have
`transform`/`perspective()`'s specific false-positive shape.
