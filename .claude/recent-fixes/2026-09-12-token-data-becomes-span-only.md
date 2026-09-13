# `Token.Data` becomes `ReadOnlySpan<char>`-only, and internal APIs converted alongside it

## What changed

`Token.Data` (`src/PeachPDF/CSS/Tokens/Token.cs`) used to expose the token's content as an eagerly
materialized `string` (`_data.ToString()`), plus a separate `DataSpan` property for the zero-allocation
slice. `Data` is now the span itself (`_data.Span`), and `DataSpan` is gone — there is exactly one
accessor, so a caller allocates only when it explicitly writes `.ToString()`. Every call site in
`src/PeachPDF/CSS` and `src/PeachPDF/Html/Core` (225 original call sites, ~50 files) was updated to
match: a comparison/dispatch site (`token.Data.Isi("keyword")`, a `switch`/pattern match, a `FindIsi`
lookup) needed no string at all; a site that genuinely stores the value long-term (a `Selector`, a
`CssFontFace`, a dictionary key) now calls `.ToString()` explicitly, once, at that boundary. Several
internal helper signatures were converted from `string` to `ReadOnlySpan<char>` where every real caller
already had a span in hand and the value was never stored past the call (`CalcParser.IsCalcFamily`,
`CssContentEngine.IsGradientFunctionName`, `CssValueParser.Named`/`IsRecognizedTransformFunctionName`/
`IsRadialModifierIdent`, `FontPaletteValueConverter.IsKeywordOrDashedIdent`,
`PrincePdfFormFieldSettingsShorthandConverter.ResolveFieldTypeKeyword`, among others).

New helpers in `src/PeachPDF/CSS/Extensions/StringExtensions.cs` support the allocation-free
comparisons this needed: `Is`/`Isi`/`IsOneOf` gained `ReadOnlySpan<char>` overloads;
`ContainsIsi`/`FindIsi`/`FindIs` let a span be checked against (or matched into the canonical casing of)
a `string[]`/`IReadOnlyList<string>` keyword table without allocating an enumerator or a lowered copy;
`ToLowerInvariantString` uses the destination-based `MemoryExtensions.ToLowerInvariant` with a
`stackalloc` buffer for the (rarer) case where a lowered value must actually be kept, replacing the
double-allocating `.ToString().ToLowerInvariant()` pattern. `Token` also gained `WithType(TokenType)`,
which reuses the token's existing backing memory to reinterpret it as a different token type (e.g. an
`@top-left`-shaped `AtKeyword` re-read as a plain `Ident` for margin-box rule parsing) with no allocation
and no loss of `TokenExtra`, replacing a `new Token(type, token.Data.ToString().AsMemory(), ...)` pattern
that both allocated and silently dropped it.

## Why (and what it actually bought)

This was a deliberate allocation-reduction pass, not a response to a measured regression — `Token.Data`
materialization was checked and ruled out as the cause of the MathML-test slowdown investigated in
[2026-09-12-mathml-tests-bypass-css-for-large-bundled-font-registration.md](2026-09-12-mathml-tests-bypass-css-for-large-bundled-font-registration.md)
(the real cause was [the bidi resolver](2026-09-12-bidi-resolver-skips-display-none-subtrees.md)).
Measured with `GC.GetTotalAllocatedBytes(precise: true)` around repeated `StylesheetParser.ParseAsync`
calls (JIT-warmed, forced full GC before the measurement window):

- 50 parses of the bundled `bootstrap.css` fixture (6756 lines, no data URIs): **20.34 MB/parse before
  → 20.23 MB/parse after** (~0.5% lower).
- 20 parses of a synthetic stylesheet with twenty `@font-face` rules each holding a 500KB base64
  `url()` data URI: **2.079 MB/parse before → 2.076 MB/parse after** (~0.1% lower, well within run-to-run
  noise).

Both are small. The reason: for ordinary CSS text, a declaration's value is materialized into a string
(or a typed value) exactly once regardless of `Data`'s type — `CssValueParser`/the value converters
build a `CssBox` property or an `IPropertyValue` from each token exactly once per parse, so there was
only ever one allocation to save per token, not several. The old double-allocating
`.ToString().ToLowerInvariant()` pattern this replaced at several keyword-matching sites (gradient
direction/shape keywords, `content()`/`string()` mode keywords, grid line names) is real, but those
sites are a small fraction of a typical parse's total token count, so their combined savings don't show
up above noise on a whole-stylesheet benchmark. The `Token.WithType`/large-url()-reread scenario in
`Token.cs`'s own doc comment (a value re-read "a handful of times") also didn't materialize as a
measurable win here, because the synthetic benchmark's `url()` tokens are each read exactly once during
parsing, same as everything else — a caller that re-reads a large `Function`/`Url` token's `Data`
repeatedly without storing it would still benefit, but that pattern didn't show up in a full-parse
benchmark built to stress it. This change is worth keeping for the allocation discipline it establishes
in the API surface (no accidental double-allocation at the sites that do re-read), not for a
whole-stylesheet number.

## Two bugs introduced and caught during the migration (both fixed, both confirmed pre-existing-behavior-preserving after the fix)

1. **`ReadOnlySpan<char> == string` compiles but performs reference/identity comparison, not content
   comparison** — unlike C# 11 pattern matching against a span constant (`case "x":`, `{ Data: "x" }`,
   which does compile to a real content comparison and needed no changes), the `==`/`!=` *operators* on
   `ReadOnlySpan<char>` compare whether the two spans reference the same memory, not whether their
   content matches. Every site across the codebase that used to read `token.Data == "literal"` when
   `Data` was a `string` (ordinary, correct string equality) silently became a near-always-`false`
   reference comparison once `Data` became a span — the compiler gives no warning, since the code is
   entirely valid IL. This was caught by a full test-suite run going from 0 failures to **350 failures**
   (colors resolving to the CSS-initial `"black"` instead of their declared value, `!important` and
   `@layer` cascade tests failing, grid/counter/calc parsing breaking) — bisected via `git stash` against
   the unmodified `main` commit to confirm each failure was a genuine regression, not pre-existing. Fixed
   by auditing every `.Data ==`/`.Data !=` site (`grep -rn '\.Data ==\|\.Data !='`) and converting each to
   `.Is(...)` (the existing ordinal-comparison span extension) — 13 sites across
   `ValueBuilder.CheckImportant` (the `!important` keyword check — the single highest-impact site, since
   it silently broke every `!important` declaration in the suite), `CalcParser`, `SelectorConstructor`,
   `StylesheetComposer`, `AspectRatioGrammar`, `ColorFunctionExtensions`, `GridPlacementShorthand`,
   `GridTemplateShorthand`, and `CssCounterEngine`/`CssValueParser`'s lambda-based `SingleOrNull` calls.
   **Trap for a future migration**: grep for `== "` / `!= "` near any span-typed value before assuming a
   `string`→`ReadOnlySpan<char>` conversion is behavior-preserving; the compiler will not catch this for
   you.
2. **A rewritten `Token.ToValue()` introduced a duplicate closing paren for `TokenType.Function`** —
   converting `string.Concat(Data, "(", _extra!.Arguments.ToText())` (three arguments, deliberately no
   trailing `)`, because `Arguments` already includes the closing-paren token in its own serialization)
   to the interpolated form `$"{Data}({_extra!.Arguments.ToText()})"` added a fourth, extra `)` that the
   original never had. This corrupted the raw-text serialization every `var(...)`-containing declaration
   goes through (`Property.TrySetValue`'s `Converters.Any` path, used whenever a value contains `var()`),
   so `color: var(--Foo)` serialized to `var(--Foo))` and every custom-property/`var()` integration test
   failed the same way (~140 failures, colors again resolving to `"black"`). Found by isolating a minimal
   repro (`StylesheetParser.Default.ParseDeclaration("color: var(--Foo)")` and inspecting the reparsed
   `Value`) rather than guessing from the cascade-level symptom. Fixed by dropping the added `)`.

Both bugs were caught before landing, not after — the full single-threaded `net8.0` suite
(`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`) went 205 build errors → 0 build
errors → 350 test failures → 142 test failures → 0 test failures across the two fixes, confirming each
one's real effect rather than assuming the build succeeding meant the change was correct.

## A third, unrelated, pre-existing defect found and characterized (fixed as a follow-up)

Auditing `TokenDataSlicingTests.cs`'s escaped-line-continuation test (which previously asserted only
`token.Data == token.DataSpan.ToString()`, an equivalence between two accessors that could both be
wrong at once) with a real content assertion surfaced that the lexer does not actually strip an escaped
line continuation (CSS Syntax 3 §4.3.7) from a string token — confirmed via `git stash` to reproduce
identically on the unmodified `main` commit, so it is not a regression from this change. Initially filed
as [issue #1024](https://github.com/jhaygood86/PeachPDF/issues/1024) and recorded as an accepted gap on
the assumption it could be deferred; that assumption didn't hold once the pinned-buggy-output test
turned out to be platform-dependent and started failing CI on two of three OSes. Actually fixed in
[2026-09-13-lexer-line-continuation-emits-environment-newline.md](2026-09-13-lexer-line-continuation-emits-environment-newline.md) —
the accepted-gap file is deleted and #1024 is closed.

## Verification

- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors, across every project and TFM
  (`PeachPDF`, `PeachPDF.Tests`, `PeachPDF.Cli`, `PeachPDF.Cli.Tests`, `PeachPDF.SourceGenerators`,
  `PeachPDF.SourceGenerators.Tests`, `PeachPDF.TestHarness`, `PeachPDF.Demo.BlazorWasm`).
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 -- xunit.maxParallelThreads=1`
  (single-threaded): 11025 passed, 0 failed, 9 skipped, ~46s — consistent with the ~43s baseline from
  [2026-09-12-bidi-resolver-skips-display-none-subtrees.md](2026-09-12-bidi-resolver-skips-display-none-subtrees.md),
  confirming this change is allocation-neutral-to-slightly-positive on wall-clock as well as bytes.
