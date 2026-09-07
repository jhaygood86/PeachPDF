# `"cssom"` is no longer a valid `cssDataType` at all

The final step of the issue #909 cssom-allocation-reduction work: after routing the large majority of
`cssom`-typed properties through cheaper validators (`cssom-grammar`, `length-list`, `ratio`,
`keyword-list`, `transform-list`, `parsed`), the remaining ~15 stragglers were converted too, and
`"cssom"` was removed from the generator entirely — `DataTypeKind.CssOm`, `PropertyModelParser`'s
`"cssom"` case, `ValidatorExpressionBuilder.BuildCssOmClause`, and the schema's enum entry are all gone.
Any future property declaring `cssDataType: "cssom"` now fails the build with PPG002 ("unknown
cssDataType"), forcing a real grammar choice.

## What the last stragglers turned out to need

Every one of the ~15 remaining properties had a real Layer A converter already sitting there, same
pattern as the earlier batch — `font-family` → `Converters.FontFamiliesConverter` (already public),
`hyphenate-character`/its `-prince-` alias → `Converters.HyphenateCharacterConverter` (already public),
`list-style-type` → `Converters.ListStyleConverter` (already public), the four `grid-column-*`/
`grid-row-*` line-placement properties → `Converters.GridLineConverter` (already public, one shared
field for all four), the three `-prince-bookmark-*` aliases → the same `Converters.BookmarkLevel/Label/
StateConverter` fields their canonical counterparts already used, and `-peachpdf-bookmark-target`/its
two Prince aliases → `Converters.BookmarkTargetConverter` (already public — an earlier investigation
pass had incorrectly concluded no such converter existed; it was sitting in `Converters.cs` the whole
time, just not grepped for by the right name). `transform-origin`, `text-indent`, and `font-palette`
needed the same one-line field-split-and-expose treatment used throughout this whole effort
(`TransformOriginProperty.ValueGrammar`, `TextIndentValueConverter.Instance`,
`FontPaletteValueConverter.Instance`).

## Two properties needed real new grammar work, not just wiring

**`grid-template-areas`/`grid-auto-flow`** used the same `CssOmGrammar` mechanism as everything else
(`GridTemplateAreasGrammar.TryParse` with `acceptsNoneLiteral: true`; `GridAutoFlowValueConverter`
gained a cached `Instance` field since it has no separate shared grammar class of its own to point at).

**`page` (page-name) was the one genuine exception to "just wire up the existing converter."** It has
**no real `PropertyFactory` registration at all** — confirmed by grepping `PropertyFactory.cs` directly,
not assumed. This means its `cssom` clause's left-hand `PropertyFactory.Instance.Create("page") is not
{} knownProperty` was *always true*, short-circuiting before ever tokenizing — i.e. `page` silently
accepted **any string** as a valid page name, and did so cheaply (a `FrozenDictionary` miss costs
nothing). There was no existing grammar to delegate to, and no perf problem to fix. Initially left as
`cssom` deliberately with a comment explaining why perf work shouldn't touch it — but removing `cssom`
as a *type* entirely meant this property needed a real answer regardless of whether it had a perf
problem. Added a new `custom-ident-or-auto` `DataTypeKind` and `CssValueParser.IsValidPageName` (a
small span-based `auto | <custom-ident>` check, matching CSS Paged Media 3 §4.2), which is a **real,
deliberate narrowing** of accepted syntax (from "any string" to "auto or a valid identifier") — not a
pure refactor. Documented as such in the property's own comment rather than described as behavior-
preserving, since it demonstrably isn't.

## The expensive lesson: `"cssom"` was used as an unrelated test placeholder ~40 times

Removing `"cssom"` from the schema broke the generator's own test suite in a way that had nothing to do
with cssom's actual behavior: `GeneratorDiagnosticTests.cs`/`GeneratorGoldenFileTests.cs`/
`GeneratorIncrementalityTests.cs` used `"cssDataType": "cssom"` as a convenient, minimal-fuss placeholder
in ~40 test JSON snippets testing entirely unrelated things (PPG005 "no propertyPath", PPG012 "unknown
area", alias-mismatch diagnostics, incrementality caching, `ComputedStyleAreas` field shape, and so on) —
none of them were testing cssom's own emitted code. Only **one** test
(`Emits_CssPropertyRegistry_For_A_Plain_Property`) actually asserted the literal
`PropertyFactory.Instance.Create(...) is not {} knownProperty || (StylesheetParser.Default.ParseValue...`
text; that one was repurposed to test the same "plain property gets Validate_/Supports_/Set_/dictionary
entries wired correctly" shape using `"length"` instead, since the shape being verified was never
specific to cssom's own grammar. Every other occurrence was swapped to `"length"` (the simplest scalar
type needing no extra JSON fields) with no loss of test coverage, since none of those tests' actual
subject matter depended on which harmless data type filled that slot.

**Before removing a "fallback"/"unconstrained" data type kind from a schema like this one, grep the
test suite for the literal type-name string first** — a widely-used placeholder value will be sprinkled
through many tests that have nothing to do with it, and distinguishing "this test is *about* the
mechanism" from "this test just needed *some* valid value here" up front is much faster than discovering
it one broken assertion at a time.
