# SVG property dispatch cutover: `SvgTreeBuilder.ApplyCommon` now calls the generated `SvgPropertyRegistry`

## What was actually done

Completed the SVG half of the CSS/SVG property registry generator migration (HTML's cutover landed
earlier the same day). Authored the remaining common SVG presentation properties into
`css-properties.json` (`stroke`, `stroke-width`, `stroke-miterlimit`, `stroke-dashoffset`,
`stroke-dasharray`, `opacity`'s `svg` binding, `fill-rule`, `fill-opacity`, `stroke-opacity`,
`stroke-linejoin` — `fill`/`stroke-linecap`/`direction` already existed from the generator's original
proof slice), then refactored `SvgTreeBuilder.ApplyCommon` to call `SvgPropertyRegistry.TrySet` for each
one instead of the hand-written `SvgValueParsers.ParseX` calls. `clip-path`, `mask`,
`marker`/`marker-start`/`marker-mid`/`marker-end`, and `direction` stay hand-written, per the original
plan's reasoning (a document-mutating side effect, a three-way shorthand-override precedence, and a
carrier-only property never stored on `SvgElement`, respectively).

## Load-bearing design decision: `ApplyCommon` keeps its own fallback logic

The original plan sketched a generic loop — "resolve the raw value, try the generated setter, and on
failure apply that property's own `invalidBehavior`" — but the generator has no notion of "the inherited
value" (that only exists as `InheritedPaint`, a builder-instance concept the generated static code can't
reach). So `TrySet`'s contract stayed exactly what the HTML side already established: parse-and-validate,
write the field only on success, otherwise leave it untouched. `ApplyCommon` keeps deciding, per property,
what "untouched" or "fall back to inherited" means — it just delegates the parse/validate step. This
works because `SvgElement`'s own constructor field defaults were already chosen (by whoever wrote the
original hand-written dispatch) to equal each property's hardcoded invalid-value fallback — e.g.
`FillRule`'s default is `Nonzero`, and `TryParseFillRule`'s own `out`-param default on failure is also
`Nonzero` — so "leave the field at its constructor default" and "reset to the old hardcoded fallback"
are the same code path for `fill`/`stroke`/`fill-rule`/`fill-opacity`/`stroke-opacity`/
`stroke-linecap`/`stroke-linejoin`. Only `stroke-width`/`stroke-miterlimit`/`stroke-dashoffset`/
`stroke-dasharray` fall back to the *inherited* value on invalid input (not a hardcoded default), so
those four check `TrySet`'s return value explicitly and assign `inherited.X` on failure.

`svg.invalidBehavior` in the JSON stays informational/documentation (and the `@supports` oracle's
future consumer) — the generator never reads it; `ApplyCommon`'s own code is what encodes it.

## Two generator gaps found and fixed along the way

1. **`SvgPropertyContext` had no viewport reference length.** The old `ApplyCommon` called
   `SvgValueParsers.ParseLength(attr, ViewportDiagonal)` for `stroke-width`/`stroke-dashoffset` so a
   percentage value resolves against the current viewport's diagonal (the SVG percentage-length
   formula) — but the generator's default `SvgLength` setter case called `ParseLength(value, null)`,
   silently rejecting every percentage value it would generate for a real property. Fixed by adding
   `ViewportDiagonal` to `SvgPropertyContext` and threading `ApplyCommon`'s own `ViewportDiagonal`
   property through to the `ctx` it constructs. Also fixed `ValidatorExpressionBuilder`'s `SvgLength`
   validator clause to match — it used to validate against `null` too, meaning `Validate_X` and `Set_X`
   would have disagreed about whether a percentage value is valid.
2. **`fill`/`stroke`'s `url()`-to-pattern-vs-gradient reclassification had no path into generated code.**
   `SvgValueParsers.TryParsePaint` has no document context, so a `url(#id)` reference always classifies
   as `GradientRef`; `SvgTreeBuilder.ResolveUrlPaintKind` reclassifies it against `_document.Gradients`/
   `_document.Patterns` (builder-instance state). Rather than exposing those dictionaries on
   `SvgPropertyContext` (which would leak SVG-internal state into generated code), added a
   `Func<SvgPaint, SvgPaint> ResolveUrlPaintKind` delegate to the context instead — `ApplyCommon` passes
   its own `ResolveUrlPaintKind` method group, the generator's default `SvgPaint` setter case calls
   `ctx.ResolveUrlPaintKind(parsed)` unconditionally (correct for every `svg-paint`-typed property, not
   just fill/stroke), and the nominal `@supports` context (`SupportsDeclaration`, which has no real
   document) passes the identity function.

## A real, pre-existing bug found and fixed while authoring the JSON

`css-properties.json` already listed `border-collapse`/`border-spacing` as `inherited: false` from the
prior (HTML-only) authoring pass — both are actually spec-inherited (and the old hand-written
`CssDefaults.InheritedProperties` correctly listed them). A pre-cutover completeness check (comparing
`CssDefaults.InheritedProperties` against the generated `CssPropertyRegistry.InheritedProperties` before
deleting the old hand-written set) caught this immediately; fixed in the JSON before the HTML cutover
landed. Recorded here because it's the kind of check worth re-running before *any* future cutover that
deletes a hand-written table in favor of a generated one — the completeness check is cheap and the
alternative is a silent, hard-to-notice inheritance regression.

## What was deliberately not done

- **`InheritedPaint` was not renamed to `SvgInheritedPaint`.** The original plan anticipated this rename
  (and the schema/`SvgBinding.cs` doc comments still say "the `SvgInheritedPaint` member…"), on the
  assumption that generated code would need to name the type. It doesn't: `ApplyCommon` is still the only
  code that ever touches `InheritedPaint`, since (per the design decision above) the generated setters
  never receive or return it. Renaming a private, single-file type used in ~15 places for a schema doc
  comment's sake wasn't worth the risk; the doc comments are the stale artifact here, not the code — a
  future editor should treat `InheritedPaint` as the real name.
- **`mask` was not moved from "manual" to "common."** Structurally it's a plain `url(#id)` reference with
  no inherited fallback (unlike `clip-path`/`marker*`), so it could plausibly become a generated `common`
  entry with `cssDataType: svg-reference`. Left alone to keep this change's scope to "swap the parse call,
  keep the surrounding structure" — reclassifying it is a separate, independent decision for a future
  change, not a prerequisite for this one.
- **`clip-rule`'s entry was dropped from the old hand-written `SvgInitialValues` table without a JSON
  replacement.** Tracing its one consumer (`GetMatchedDeclarations`, used to resolve the CSS-wide
  `initial` keyword in a `<style>` block) against where `clip-rule` is actually read
  (`SvgTreeBuilder.cs`'s clip-path shape building, via a raw `node.GetAttribute("clip-rule")` call that
  never goes through `ResolveStyledAttr`/`GetMatchedDeclarations` at all) showed the old table entry was
  already unreachable dead code — `clip-rule: initial` in a `<style>` block could never have taken effect
  even before this change. Confirmed via the existing `SvgCssDomNodeTests` theory (no `clip-rule` case
  exists, and nothing regressed).
- **`SvgReference`/`SvgTransform` `DataTypeKind`s remain unimplemented in the generator** (`SymbolValidator`
  and `PropertyModelParser` recognize the JSON spelling `svg-reference`/`svg-transform`, but
  `RegistryEmitter`/`ValidatorExpressionBuilder` throw `NotSupportedException` if an entry actually uses
  one) — not needed since `mask`/`marker*`/`clip-path`/`transform` all stayed manual.

## Evidence

- `CssPropertyRegistrySweepEquivalenceTests.cs`/`CssPropertyRegistryEquivalenceTests.cs` (HTML's temporary
  differential harness) deleted — now a tautology since `CssUtils` forwards directly to
  `CssPropertyRegistry`; `SvgPropertyRegistryEquivalenceTests.cs` extended with one theory per newly
  authored SVG property (60 cases total, including a dedicated case proving `ctx.ResolveUrlPaintKind`
  actually gets invoked, and every length-shaped property tested with a percentage value against a
  non-null `ViewportDiagonal`).
- Full `net8.0` suite: 7597 passed, 0 failed, 9 skipped. `PeachPDF.SourceGenerators.Tests`: 51 passed.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- `dotnet publish PeachPDF.Cli -p:PublishAot=true`: succeeds, binary runs.
- Diff coverage against the prior commit: 100% (57/57 changed lines).
- **Two-renderer, before/after verification**: generated every showcase PDF from the pre-refactor
  commit and the post-refactor working tree (via `git stash`), and diffed each SVG-relevant showcase's
  per-page PDF content stream (`fitz`'s `page.read_contents()`) — byte-identical on every page of `svg`,
  `svg_text_advanced`, `opacity`, `gradients`, and `marker_styling` (the whole-PDF byte diff was
  metadata-only: `/CreationDate`/`/ID`). Also rasterized the dash-array/linecap/linejoin/pattern-fill
  page through both PDFium and MuPDF and visually confirmed both agree with the pre-refactor render.
