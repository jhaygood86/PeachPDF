# SVG per-element `lang`/`xml:lang`-driven GSUB language selection

Closes the last of PR1's own deferred gaps: SVG `<text>`/`<tspan>`/`<tref>`/`<textPath>` now resolves
each script's language-specific `LangSys` from an element's own `lang`/`xml:lang` (or its nearest
ancestor's) the same way HTML text already did via `CssBox.Language`, instead of always falling back to
the script's default language system.

## Load-bearing ideas

- **`ISvgSourceNode` gained one new default-implemented member, `DocumentLanguageFallback => null`,**
  rather than a parallel "resolve my own inherited language" API duplicating what `CssBox.Language`
  already does. An inline `<svg>`'s wrapping `CssBoxSvgSourceNode` overrides it to return `_box.Language`
  (the surrounding HTML document's own already-fully-resolved inherited language, own-`lang`-or-nearest-
  ancestor's-or-`DocumentLanguage`) - a standalone `XElementSvgSourceNode` document has no such outer
  context and keeps the interface default (null), meaning its own SVG-tree `lang`/`xml:lang` inheritance
  is the entire story. This mirrors the same split `contextColor` (SVG's `currentColor` seed) already
  uses - "ask the wrapping source for the one piece of outer-document context I can't derive from the
  SVG tree alone" is an established pattern here, not a new one.
- **The SVG tree's own `lang`/`xml:lang` inheritance is a tenth `FontContext` field**, resolved in
  `SvgTreeBuilder.ComputeFontContext` alongside the other nine, seeded at the root from
  `root.DocumentLanguageFallback` (`FontContext.Default with { Language = root.DocumentLanguageFallback }`
  in `BuildDocument`) so an inline SVG's own root-level `lang`/`xml:lang`, if present, still wins over the
  HTML fallback - the normal "own value, else inherited" resolution every other `FontContext` field
  already uses handles that for free, with no special-casing needed at the root.
- **`lang`/`xml:lang` are read via plain `ISvgSourceNode.GetAttribute`, never `ResolveStyledAttr`** - they
  are XML/HTML attributes, not CSS-styled properties, so routing them through the `style=""`/matched-rule
  cascade tiers `ResolveStyledAttr` implements would be actively wrong (nothing in CSS defines `lang` as a
  property). SVG2's own unprefixed `lang` is checked before the legacy `xml:lang`, mirroring this
  codebase's existing `href`-before-`xlink:href` precedence for `tref`/`textPath`/`use`.
- **`xml:lang` needed one new namespace-aware special case in `XElementSvgSourceNode.GetAttribute`**,
  exactly parallel to its existing `xlink:href` one: `System.Xml.Linq`'s `XElement.Attribute(string)`
  treats a bare string as an unnamespaced `XName`, so `"xml:lang"` would silently never match the
  namespace-qualified attribute `XDocument.Parse` actually produces for it - `XNamespace.Xml + "lang"`
  (a built-in constant for exactly this reserved namespace) is the fix. `CssBoxSvgSourceNode` needed no
  equivalent special case: an inline `<svg>` is parsed by the HTML tokenizer, which has no notion of XML
  namespaces at all, so `xml:lang` already arrives as a literal attribute name there.

## What was found by running it, not by reading it

- Nothing surprising turned up - the design generalized cleanly from the CSS Text/Fonts parity work
  earlier in the same PR (issue #533), and all 9 new tests (including the two exercising
  `CssBoxSvgSourceNode`'s HTML-fallback path via a real `HtmlParser.ParseDocument` + `DomUtils.
  GetBoxByTagName` round trip) passed on the first run.

## What was deliberately not done, and why

- No change to `OpenTypeLanguageTags`'s curated BCP-47 subset, or to the "language absent from the table
  falls back to the script's `DefaultLangSys`" behavior - both are pre-existing, HTML-and-SVG-shared
  mechanics this change reuses as-is (see `.claude/accepted-gaps/no-text-shaping.md`'s own note on that).

## Evidence

- New file `src/PeachPDF.Tests/Svg/SvgTextLanguageTests.cs` (9 tests): no-lang default, `lang` alone,
  `xml:lang` fallback, `lang` precedence over `xml:lang`, inheritance through a non-text `<g>` ancestor,
  a `<tspan>` override, `lang=""` falling through (matching `CssBox.Language`'s own simplification), and
  the two `CssBoxSvgSourceNode` HTML-fallback cases - each asserting the resolved `TextShapingFeatures.
  Language` value actually reaching `RGraphics.DrawString`, not just that it parses.
- Full suite (`dotnet test --framework net8.0`): 9570 passed, 0 failed, 9 pre-existing skips - no
  regressions (652 SVG tests, up from 643).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Updated `.claude/accepted-gaps/svg-text-decoration-v1-scope.md` (removed the now-closed per-language
  bullet) and the SVG-parity sentences in `docs/html-css-support.md`/`docs/supported-svg-features.md`
  that previously called this gap out.
