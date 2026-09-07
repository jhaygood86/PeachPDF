# Allocation-free CSS length tokenization, Stage 1 (issue #910)

## The load-bearing idea

`CssValueParser.GetUnit` ran the full CSS tokenizer (`Lexer` + `TextSource` + `Token` object graph)
just to pull a number and unit out of strings like `"12px"`, and it's reached from ~192 call sites
across 24 files in the hottest per-box, per-layout-pass code. Rather than bolt a hand-rolled bypass
parser in front of the real tokenizer (the shape issue #910 itself proposed), this fixes the
*scaffolding* the real tokenizer pays for on every call regardless of input, then adds a fast path
built from grammar genuinely shared with (not duplicated from) the real `Lexer`:

1. **`TextSource`'s string constructor no longer chains through the stream-oriented private
   constructor.** It used to unconditionally allocate a `byte[4096]`, `char[4097]`, a `MemoryStream`,
   and a `Decoder` - none of which a string source ever touches (every reader of those fields is
   gated on `!_finished`, and the string ctor sets `_finished = true` immediately) - then copy the
   whole input string a second time into a pooled `StringBuilder` purely so `TextSource` could do
   `StringBuilder`-style random access a `string` already supports natively. Now a `_sourceText` field
   is used directly, and every member (`Text`, indexer, `Length`, `ReadCharacter()`,
   `ReadCharacters(int)`, `Dispose()`, `CurrentEncoding`, `PrefetchAllAsync`) branches on
   `_sourceText != null`. A pleasant side effect: `Dispose()` becomes a true no-op for a string source
   (nothing was ever allocated for it), which matters for point 3 below.
2. **`Lexer`/`TextSource` are now actually disposed at the hot call sites** (`CssValueParser.
   GetCssTokens`, and 5 of `StylesheetParser`'s call sites that go through `CreateTokenizer`) - they
   never were before, so `Pool`'s `[ThreadStatic]` `StringBuilder` pool (which exists specifically to
   soften this) almost never got its instances back.
3. **`ParseLength`'s box-aware overload no longer re-parses the same string twice** - it used to call
   `GetUnit` (tokenizer-based) and then unconditionally also call `Length.TryParse`/`StylesheetUnit`
   (a second, independent scan) just to read `NeedsPixelsPerPointCatchUp`. A new private `ParseLength`
   overload threads a `bool?` out-parameter through instead, reusing the unit `GetUnit` already
   classified whenever the string actually had one written into it.
4. **New `NumberSyntax.TryConsumeNumber`** (`src/PeachPDF/CSS/Parser/NumberSyntax.cs`) plus
   `CssValueParser.TryClassifyLengthFast`: an allocation-free classifier for the "number+unit",
   "number+%", and "bare number" shapes, falling through to the unchanged, full `GetCssTokens` path
   for anything it isn't confident about (embedded whitespace, escapes, multi-token input, etc.) - so
   the tokenizer stays the one authority for everything this shortcut doesn't recognize.

## What was found by running it, not by reading it

**`Lexer.NumberExponential`/`SciNotation` are dead code today, discovered while writing
`NumberSyntax`.** `NumberRest`/`NumberFraction`'s main scanning loop treats *any*
`CharExtensions.IsNameStart` character - which includes `'e'`/`'E'`, indistinguishable from any other
unit-starting letter - as the start of a unit-ident token *before* the switch statement housing
`case 'e': case 'E': return NumberExponential(...)` is ever reached. So `"1e2"` tokenizes today as
**two** tokens (`Dimension("1","e")` + `Number("2")`), never as the single scientific-notation
`Number(100)` CSS Syntax Level 3 §4.3.13 describes. This is a real, pre-existing spec-compliance gap,
confirmed by static tracing and left deliberately unfixed here - fixing it would be an observable
parsing-behavior change, out of scope for a change whose entire premise is "provably identical
output." `NumberSyntax.TryConsumeNumber` was written to match this real (if non-conformant) behavior
exactly - it does not consume an exponent - rather than the spec's own grammar, with a doc comment
explaining why so a future reader doesn't "fix" the mismatch without noticing it changes behavior.
Filed as [#921](https://github.com/jhaygood86/PeachPDF/issues/921).

**A second, genuinely load-bearing bug was caught by the project's own test-writing discipline, not
by reading the diff.** The first version of `TextSource.ReadCharacter()`'s string-backed branch only
advanced `Index` when a real character was returned, unlike the original stream-backed branch (and
unlike `ReadCharacters`), which always advances `Index` even past the end. This broke
`LexerBase.NormalizeForward`'s CRLF lookahead specifically when a source string's very *last*
character is a lone `\r` (no trailing `\n`): the peek-and-give-back logic decremented `Index` from the
wrong starting point, landing one position short of the true end. Since `Advance()` only re-reads once
`Current != EndOfFile`, this left the lexer's cursor able to re-read the same `\r` on a later `Get()`
call instead of reaching `EndOfFile` - an infinite loop for any caller that loops to `EndOfFile`, which
`CssValueParser.GetCssTokens`'s own `do { ... } while (token.Type != TokenType.EndOfFile)` does. Fixed
by matching the stream branch's unconditional-advance shape exactly; regression coverage added at
three layers (`TextSourceTests.ReadCharacter_PeekPastEndAfterReadingLastCharacter_LeavesIndexAtTrueEnd`,
`CssTokenizationTests.LexerOnlyCarriageReturn_PositionsAtTrueEndOfInput`,
`CssTokenizationTests.GetCssTokens_TrailingLoneCarriageReturn_DoesNotHangAndProducesNoTokens`).

**Measured, not assumed, allocation win**: `GC.GetAllocatedBytesForCurrentThread()` deltas over 50,000
iterations (Release build) comparing the new `ParseLength` fast path against `GetCssTokens` (the path
`GetUnit` unconditionally took before this change, for the same input string):

| input | fast path | old always-tokenize path | reduction |
| --- | --- | --- | --- |
| `"12px"` / `"1.5em"` | 32 B/call | 424 B/call | 92.5% |
| `"50%"` / `"0"` | 0 B/call | 376-392 B/call | 100% |

## Deliberately not done

**`Token.Whitespace`/`Token.Comma` singleton reuse in `Lexer.cs` was investigated and deliberately
skipped**, not just for the `.Position` reason a first pass considered, but a more concrete one:
`Token.Whitespace`'s `Data` is hardcoded to a single space, while the real per-character token
preserves whatever whitespace character was actually consumed (tab/CR/LF/FF). `TokenValue.ToCss()`/
`.Text` (via `ValueExtensions.ToText`) joins `token.ToValue()` - i.e. `Data` - across a value's
retained tokens (`ValueBuilder.Apply`'s `TokenType.Whitespace` case buffers and keeps whitespace
tokens), so swapping in the singleton would silently normalize non-space whitespace in reconstructed
CSS text. Not attempted.

**The `Token`/`List<Token>` class hierarchy itself stays heap-allocated** for the general (non-length,
non-`GetUnit`) tokenize path - idents, strings, urls, functions all still go through the same
`Lexer`/`Token` object graph as before. Filed as
[#922](https://github.com/jhaygood86/PeachPDF/issues/922) for whoever picks up the full struct/span-
based rewrite; explicitly not attempted here given the size (~35 call sites, ~12 test files that
pattern-match on concrete `Token` subtypes) and risk relative to this stage's mechanical, low-risk
scope.

**`GetUnit`'s `PlainNumber` classification result is computed but discarded** - `ParseLength` still
calls a second, independent `ParseNumber(length, hundredPercent)` for a bare unitless number. Not a
regression (this double-parse predates this change), just a missed reuse opportunity for a case
explicitly outside "the single most common length shapes" this stage targeted.

## Evidence

Full net8.0 suite: 10019 passed / 0 failed / 9 skipped (pre-existing platform-specific skips). Diff
coverage 100% (gate is 90%) against `main`. Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild` across
every project in the solution. A dedicated post-change review pass (C# conventions, current C#/.NET
APIs, CSS Syntax Level 3 compliance for the newly-added grammar code) found no correctness issues.
