# `Token` split into a small common struct + side-allocated `TokenExtra` (issue #922, final design)

## The load-bearing idea

Continuing from
[`2026-09-08-css-token-list-pooling-and-ireadonlylist-value-converter-pipeline.md`](2026-09-08-css-token-list-pooling-and-ireadonlylist-value-converter-pipeline.md),
that entry's showcase-suite benchmark left `Token` at 48 bytes (`[StructLayout(LayoutKind.Explicit)]`,
overlapping the four mutually-exclusive former-subclass fields into two shared slots) and ~+1.8% more
allocation than `main`. An intermediate step further shrank it to 48→40 bytes by moving `Quote`/
`_isValid` into the same overlap and re-verified the explicit-layout GC-aliasing behavior - that attempt
is **not** what shipped and is not in the diff; it's mentioned here only because the real fix replaces it
entirely with a different structural idea, described below.

**Real per-`TokenType` counts, gathered from the 110-showcase corpus** (temporary `[ThreadStatic]`
counters on `Token`'s constructors, added and fully reverted before landing) showed only six token kinds
- `String`, `Url`, `Comment`, `Function`, `Range`, `Dimension` - ever need more than `Type`/`Position`/
`Data`: 36,385 of 637,269 constructed tokens, **5.71%**. (`Percentage` was miscounted as needing storage
in the first pass - its unit is always the literal `"%"`, so once that's special-cased it needs nothing
stored at all.) That ratio is why a *single, usually-null reference field* pointing at a separately
allocated `TokenExtra` (`src/PeachPDF/CSS/Tokens/TokenExtra.cs`, plain `init`-only properties: `IsValid`,
`Quote`, `ExtraOrRangeStart`, `RangeEnd`, `Arguments`) beats paying the union's size on every token: `Token`
itself drops to four fields - `Type`, `Position`, `_data`, `_extra` - and **`Unsafe.SizeOf<Token>()` measures
40 bytes**, with ordinary automatic layout (no `[FieldOffset]`, no GC-aliasing risk to verify - that
entire category of risk the explicit-layout design carried is gone). Every `NewXxx` factory and public
accessor (`Unit`, `FunctionName`, `RangeStart`, `RangeEnd`, `Arguments`, `ArgumentTokens`,
`AddArgumentToken`, `IsValid`, `Quote`) keeps its exact prior signature and semantics, reading through
`_extra?.X` instead of a direct field.

## What was found by measuring it, not by assuming it

**The whole-pipeline showcase benchmark (`GC.GetAllocatedBytesForCurrentThread()` around
`PdfGenerator.GeneratePdf` for all 110 showcases) is the wrong instrument for judging whether the
*tokenizer* is allocation-free, and this cost real time to realize.** After landing the `TokenExtra`
split, that benchmark still showed **+0.271%** more allocation than `main` (down from +0.7% with the
48-byte design, but still not the clear win the whole line of work was chasing) - because tokenizer
allocation is a small fraction of a `GeneratePdf` call's total (layout, font shaping, painting, PDF
writing dominate), so a real, large *relative* improvement in tokenizing is invisible against that much
noise. Isolating allocation to just `Lexer.Get()` (temporary `[ThreadStatic]` byte/call counters wrapping
the method, reverted before landing) told the actual story, run back-to-back against `main`'s pre-struct
class hierarchy in the same environment:

| | `main` (`Token` class hierarchy) | This branch (`Token` struct + `TokenExtra`) |
|---|---:|---:|
| `Lexer.Get()` calls | 637,194 | 637,194 |
| Calls that allocate | 637,194 (100%) | 36,514 (5.73%, matches the measured `TokenExtra` ratio) |
| Calls that allocate zero bytes | 0 | 600,680 (**94.27%**) |
| Total bytes allocated by the tokenizer | 77,499,032 | 20,854,752 |

**73.1% less tokenizer allocation, and 94.27% of all tokens now cost exactly zero bytes to produce** -
against `main`'s 0%, since every token there was a separate class instance. That is the real result of
issue #922, and the whole-pipeline percentage several earlier benchmark passes chased was measuring
something else almost entirely.

**A three-way whole-suite comparison** (`v0.9.17` tag, current `origin/main`, and this branch rebased onto
`origin/main`; 3 runs each, same environment) puts this in context: `v0.9.17` → `main` dropped total
showcase-suite allocation by ~67.6% (unrelated perf work landed between the two - property-value
memoization, cheaper fast paths, etc., not this issue), and this branch vs. `main` is allocation-neutral
(+0.273%, consistent with the whole-pipeline number above) but **3.33% faster in wall time** - fewer,
smaller heap objects appear to reduce GC/copy overhead enough to show up in elapsed time even where total
bytes read as flat.

## Deliberately not done

**The remaining 5.73% (`String`/`Url`/`Comment`/`Function`/`Range`/`Dimension`) was not chased to zero.**
Those kinds carry genuinely variable-length data - escaped string content, function argument lists,
dimension units, range bounds - that has to be stored somewhere; reaching literal 100% would need a
fundamentally different approach (e.g. an arena/pool-backed `TokenExtra`, or restructuring `Function`
arguments away from a heap `List<Token>`), not attempted here.

**No pooling of `TokenExtra` objects** - each one is owned by exactly one `Token` for that token's full
lifetime (through `TokenValue`/stored declarations), with no natural "return to pool" point the way the
short-lived scratch `List<Token>` pooling (`Pool.NewTokenList`) had. Worth revisiting only if a future
measurement shows `TokenExtra` allocation itself, not the struct-size reduction, dominating a regression.

## Evidence

Full net8.0 suite: 10,261 passed / 0 failed / 9 skipped, unchanged from the prior design's baseline (one
test replaced: `Token_ExplicitLayoutSize_Is48Bytes` → `Token_CommonStructSize_Is40Bytes`;
`OverlappingFields_SurviveGcAndListResize_ForMixedTokenKinds` → `TokenExtraFields_SurviveGcAndListResize_ForMixedTokenKinds`,
extended to also cover `String`/`Comment`). Diff coverage 98% (gate is 90%). Zero-warning
`dotnet build PeachPDF.slnx -t:Rebuild` across every project, both target frameworks. `git diff --stat`
for this specific change touched only `Token.cs`, the new `TokenExtra.cs`, and the one test file - no
other file in the codebase needed adjustment, confirming the same "fully self-contained" property the
64→48-byte attempt had. The whole-pipeline, tokenizer-isolated, and three-way benchmarks above were all
run via temporary instrumentation in `PeachPDF.TestHarness`/`Lexer.cs` (and a temporary
`InternalsVisibleTo` on `PeachPDF.csproj` where needed), verified reverted (`git diff --stat` clean)
before landing.
