# Allocation-free CSS tokenizer: `Token` struct conversion and slice-backed `Data` (issue #922)

## The load-bearing idea

Issue #922 asked to eliminate the `Token` class hierarchy's per-token heap allocations. Investigation
found a partial, per-kind conversion isn't possible (a struct can't derive from `Token`, and
`List<Token>` needs one homogeneous element type), so the whole 9-class hierarchy (`Token` base +
`UnitToken`/`NumberToken`/`KeywordToken`/`StringToken`/`ColorToken`/`RangeToken`/`UrlToken`/
`CommentToken`/`FunctionToken`) was collapsed into one discriminated-union `readonly struct Token`
(`src/PeachPDF/CSS/Tokens/Token.cs`), keyed by `TokenType`. This alone removes N heap objects (and N GC
roots) per tokenize call in favor of one contiguous `List<Token>` backing array - the dominant win.

On top of that, `Token.Data` moved from an eagerly-materialized `string` to a
`ReadOnlyMemory<char> _data` field: `Data => _data.ToString()` and a new `DataSpan => _data.Span`. Most
tokens now hold a slice of `TextSource`'s already-alive backing buffer instead of a freshly allocated
substring - `Data` costs one allocation only if and when something actually reads it as a `string`
(`DataSpan` never allocates), rather than unconditionally at tokenize time. No separate "owned string"
field was needed to get this for free: `ReadOnlyMemory<char>.ToString()` already returns the same
`string` instance with zero extra allocation whenever it spans an entire string unchanged, which is
exactly what wrapping an owned string via `.AsMemory()` produces. `ReadOnlyMemory<char>`, not
`ReadOnlySpan<char>`, is what makes this possible at all: unlike `Span`, `Memory` is not a `ref struct`,
so `Token` stays an ordinary value type storable in `List<Token>`/`TokenValue`/
`IPropertyValue.Original` exactly as before - nothing about their retention model changed.

Two things must still force an owned-string fallback instead of a slice, both handled by
`Lexer.EndContent()` checking a `_mustMaterialize` flag (set by `AppendEscape`/`AppendLineContinuation`)
and `LexerBase.CrossedCarriageReturn` (set by `NormalizeForward`): (1) **escape sequences** - decoded
content never matches the literal source bytes it came from; (2) **any raw `\r` the scan crossed** -
`NormalizeForward` collapses *every* raw `\r` to `\n`, not just a `\r\n` pair, so both a CRLF pair
(wrong length) and a lone CR (right length, wrong character) diverge from a literal slice. A third,
less obvious case surfaced while implementing this: **`StringBuilder.AppendLine()`** (used for CSS's
string/url escaped-line-continuation feature, `\` immediately followed by a newline) inserts the
platform's default line terminator, not the source's own newline - `AppendLineContinuation()` wraps
that call and also sets `_mustMaterialize`.

Part 2 (`CssStreamLoader.cs`, new) moved BOM sniffing/decoding for `@import`/externally-fetched
stylesheets off `TextSource` entirely, using `System.IO.Pipelines.PipeReader` for pooled buffering
instead of `TextSource`'s old hand-rolled `byte[4096]`/`char[4097]`/`MemoryStream`/`Decoder` quartet.
`TextSource` itself collapsed to a single `ReadOnlyMemory<char>`-backed cursor mode (no more
`Stream`-aware branches) - the same `Slice(start, length)` the range-backed `Token.Data` design needs is
what `Lexer` calls to hand each token its piece of that memory. `TextSource.CurrentEncoding`'s "peek
without consuming, replay from raw bytes if the encoding turns out wrong" mechanism was confirmed to
have zero callers (grepped every use before deleting it), so `CssStreamLoader` doesn't replicate it -
BOM detection just decides once from the first chunk and decodes the rest with the resulting `Decoder`.

Parts 3a/3b (already landed on `main` before this entry, see the two 30-day-expired fix notes this one
supersedes): `SingleCharStrings` (a static `char → string` lookup table covering the whole `char`
domain, for whitespace/delimiter single-character tokens) and `AppendEscape` (decodes a CSS escape
directly into `StringBuffer` via `stackalloc`/manual surrogate-pair math, no scratch-string allocation).

## What was found by running it, not by reading it

**`TextEncoding.Utf32Le`/`Utf32Be` were silently `UTF8Encoding` on this runtime, a pre-existing bug
newly exposed by writing `CssStreamLoaderTests`'s UTF-32 BOM tests** (the first tests in this codebase
to exercise an actual UTF-32 stream decode - `TextSourceTests.cs` had zero stream-constructor coverage
before Part 2). `TextEncoding.GetEncoding(string name)` gates `Encoding.GetEncoding(name)` behind
`AvailableEncodings.Contains(name)` (populated from `Encoding.GetEncodings()`), and that enumeration
registers `"utf-32"`/`"utf-32BE"` as its canonical names, not `"UTF-32LE"`/`"UTF-32BE"` - so the gate
silently failed and `GetEncoding` fell back to `Utf8`, even though `Encoding.GetEncoding("UTF-32LE")`
resolves fine when called directly (bypassing that gate). Confirmed via a throwaway console app printing
`Encoding.GetEncodings().Select(e => e.Name)`. Fixed by constructing `UTF32Encoding` directly
(`new UTF32Encoding(bigEndian, false)`), mirroring how `Utf16Be`/`Utf16Le` were already constructed
directly via `new UnicodeEncoding(...)` rather than by name lookup - the one code site in `TextEncoding.cs`
that skipped that existing convention.

**`Lexer.NumberExponential`'s fallback branch (not a valid exponent, e.g. `"1e"` or `"1e+"`) produced a
wrong slice, caught by the existing `CssTokenizationTests` suite immediately (not a new test)**: it
re-appends `letter` (`'e'`/`'E'`) as the first character of a dimension's unit after having read one or
two characters further ahead to check for a valid exponent that didn't pan out. Appending via
`AppendLiteral` (which always trusts *live* `Source.Index`) there wrongly folded that failed lookahead
into the tracked content-end - `"1e"` sliced `Source[1,3)` = `"e\0"`-adjacent garbage instead of `"e"`
(`ArgumentOutOfRangeException` from `TextSource.Slice`, since the range extended past a since-consumed
EOF sentinel position), and `"1e+"` sliced `"e+"` instead of `"e"`. Fixed with a dedicated
`AppendLiteralAt(char, int position)` that takes the character's *known* position instead of trusting
the cursor - the only append site in the whole rewrite where the generic "trust `Source.Index`" rule
doesn't hold, since it's the only place a character gets appended as the *sole, final, uncorrected*
write for its run after intervening lookahead reads that were then abandoned (every other multi-append
call site either appends characters in strict left-to-right read order with no abandoned lookahead in
between, or is immediately followed by a fresh-read append that overwrites the same field with the
objectively correct final value - both self-correct without needing an explicit position).

**`AppendLiteral`'s "just track `_contentEnd = Source.Index` on every real append" design was chosen
specifically because it sidesteps a whole class of position-reconstruction bugs a first draft ran into.**
An earlier approach tried to derive each token's end position *retroactively* by counting how many
lookahead `GetNext()`/`Back()` calls happened between the content's true end and the terminal
`FlushBuffer()`/`EndContent()` call site - e.g. `Lexer.Comment()`'s "found closing `*/`" exit reads two
characters (the `*` and the `/`) past the last real content character before returning, so a naive
"slice up to live `Source.Index`" would wrongly include part of the delimiter. Manually re-deriving the
right subtraction at each of ~15 terminal call sites was exactly the kind of fragile, easy-to-get-wrong
reasoning the plan's own "Open question" flagged as a risk. Tracking `_contentEnd` *proactively* -
advanced only by `AppendLiteral`/`AppendLiteralAt`, never by a bare read - makes every terminal call
site's `EndContent()` correct by construction, including the EOF case (`Source.Index` can run past
`Source.Length` once `Symbols.EndOfFile` has been read, since `TextSource.ReadCharacter` still
increments `Index` past the end - `_contentEnd` never advances there either, since EOF is never
appended).

## Deliberately not done

**Parts 3d-3g of the original plan** (pooling `GetCssTokens`'s top-level `List<Token>` at audited-safe
call sites; `StylesheetParser`/`StylesheetComposer`'s `Tuple`→`ValueTuple`, per-declaration delegate
hoisting, and lazy/pooled per-block `Dictionary`; `ValueExtensions.cs` retyped to
`IReadOnlyList<Token>`; `ExtractFor`'s shorthand-splitting comparisons migrated to `Token.DataSpan`
across ~30 `ValueConverters/*.cs` files) - not started this session. Each is independent of the
struct/slice work landed here and lower-risk (no `Lexer.cs` scan-method surgery), left for a follow-up
pass.

**A dedicated golden-token-stream oracle test file was not written separately from the existing
suite.** The plan's original verification design called for snapshotting the *pre-rewrite* tokenizer's
output as a fixed oracle before starting; by the time this session picked the work back up, Part 1 (the
struct conversion itself) had already landed with the existing ~3800 CSS tests as its regression net.
Given that net already caught the `NumberExponential` bug above on the very first run, and
`TokenDataSlicingTests.cs`/`CssTokenizationTests`'s new cases specifically assert the
`Data`/`DataSpan` equivalence invariant across a representative corpus (plain content, CRLF, lone CR,
escapes, line continuations, and the exponent-fallback edge cases), a fresh oracle capture was judged
redundant rather than skipped for expedience.

## Evidence

Full net8.0 suite: 10251 passed / 0 failed / 9 skipped (pre-existing platform-specific skips), run
repeatedly; one single flaky failure was observed once across ~7 total runs and did not recur, matching
this repo's documented `PeachPDF.Fonts.FontFactory` static-cache parallel-test-flakiness risk (unrelated
to this change - nothing here touches fonts). Diff coverage 97% (gate is 90%) against `main`. Zero-warning
`dotnet build PeachPDF.slnx -t:Rebuild` across every project, both target frameworks. New tests:
`CssStreamLoaderTests.cs` (BOM detection for all 6 supported encodings including the UTF-32 fix above,
multi-segment `PipeReader` decode, a multi-byte character split across a buffer boundary),
`TokenDataSlicingTests.cs` (the `DataSpan`/`Data` equivalence invariant, CRLF/lone-CR/escape/
line-continuation divergence), and ~30 new `CssTokenizationTests` cases targeting the rare
recovery/escape/EOF branches the redesign touched in every scan family (strings, hashes, at-keywords,
numbers/dimensions, urls, unicode-ranges, comments).
