# `clip-path: path(...)` support

_Landed 2026-09-10._

Added the `path()` basic shape to `clip-path` ([CSS Shapes Level 1
§path()](https://www.w3.org/TR/css-shapes-1/#funcdef-path)), the last of the four gaps tracked by
[issue #217](https://github.com/jhaygood86/PeachPDF/issues/217) that was actually closable (the
other three - `inset()`'s rounded corners, non-default `<geometry-box>`, and `url()` - are now their
own files under `.claude/accepted-gaps/`, migrated out of the stale 2026-07-22 recent-fix entry this
one replaces).

**Load-bearing idea:** `path()`'s argument is literally the SVG path-data mini-language, and
PeachPDF already had a complete parser for it (`SvgPathDataParser`, used for `<path d="...">`,
`<textPath>`, markers) - so this was reuse, not new-parser work, following the repo's "one grammar,
one parser" rule. The one real design problem: CSS Shapes is explicit that a `path()` string which
doesn't conform to SVG 1.1's grammar, or that conforms but describes an empty path, invalidates the
*entire* `clip-path` declaration (not just a no-op clip) - and `SvgPathDataParser.Parse` had no way
to report that; it silently stops and returns whatever it parsed so far, which is the right behavior
for `<path d>` but wrong for `path()`. Fixed by refactoring the single scan loop into a private
`ParseCore` returning a well-formedness bool, with `Parse` (existing lenient callers, unchanged
behavior) and a new `TryParse` (used by `BasicShapeGrammar.ParsePath`) both built on top of it - one
pass over the string either way, and `Parse`'s existing callers see zero behavior change.

**Also learned during review:** the natural design put path-string validation in
`BasicShapeGrammar.ParsePath` (`PeachPDF.CSS`) rather than deferring it to the render-time resolver,
since the validity check has to run wherever the shared grammar runs (both the CSS-OM
`ClipPathValueConverter` and the paint-time `CssClipPathResolver` call `BasicShapeGrammar.TryParse`)
- and that means `PeachPDF.CSS` now references `PeachPDF.Svg` for this one parser. That's a new
edge between those two namespaces, but not a new *kind* of coupling: `Html/Core` and `Svg` already
reference each other within the single `PeachPDF` assembly, and `Svg` already references
`PeachPDF.CSS` for tokens/value parsing.

**Deliberately not done:** `url(#id)` (referencing an SVG `<clipPath>` element) is still
unsupported - a different mechanism (external reference vs. inline shape) worth its own change, now
tracked in `.claude/accepted-gaps/clip-path-url-source-unsupported.md`.

**Evidence:** `BasicShapeGrammarTests`/`SvgPathDataParserTests` cover the grammar and the new
`TryParse` well-formedness contract (including the spec's "conforms but empty" case);
`ClipPathResolverIntegrationTests`/`ClipPathPaintIntegrationTests` cover coordinate resolution
(including the `PixelsPerPoint` division discipline and a diametrically-opposed arc, per
`.claude/invariants/svg-diametric-arc-direction-comes-from-sweep-flag.md`) and the paint-time push/
pop bracketing. Full suite green (10,602 passed) on net8.0; 100% diff coverage on the changed lines
(`diff-cover` against `main`); solution rebuild clean at 0 warnings. The updated `clip_path` showcase
(a heart traced with `M`/`C`/`Z` path data) was rasterized through both PDFium and MuPDF and the two
renders agree.
