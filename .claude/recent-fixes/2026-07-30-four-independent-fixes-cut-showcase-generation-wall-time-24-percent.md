# Four independent wasted-work fixes cut showcase-generation wall time ~24%

## The load-bearing idea

Continuing the same day's `CascadeApplyStyles` defaulting-skip work (see the sibling entry), a fresh
`dotnet-trace` CPU profile plus a `dotnet-trace --profile gc-verbose` allocation profile of the full
73-showcase `PeachPDF.TestHarness` run surfaced four more, independent instances of the same pattern:
code paying full cost for work whose result was either discardable or already knowable cheaply. None
of the four touch each other's code; they were found and fixed in the order the profiles pointed at
them, each re-measured before moving to the next.

1. **`PdfGenerator.AddPdfPages`'s `ShrinkToFit`/`ScaleToPageSize` arm ran its full second
   `SetContent` (HTML/CSS re-parse) + second `PerformLayout` pass unconditionally**, even when the
   measuring pass already found the content fits at the current `PixelsPerPoint` - the common case,
   since an auto-width block box already stretches to its container's available width; a rescale is
   only needed when content genuinely overflows (a wide table, `white-space: nowrap`). Fixed by
   computing the effective `PixelsPerPoint` first and gating the font-cache clear / re-parse / second
   layout pass on `NeedsRescale(current, effective)` actually being true - a new small pure function,
   directly unit-tested. 39 of 70 `SaveShowcaseAsync` call sites use `ShrinkToFit = true`.

2. **`PeachPDF.CSS.Pool` guarded three unrelated `Stack<T>` rent/return pools with one shared,
   process-wide `lock`.** A CPU profile showed `Monitor.Enter_Slowpath` - almost entirely under
   `Pool.NewStringBuilder()` - responsible for 37% of the whole run's CPU self-time, ahead of layout,
   cascade, and PDF writing combined. A CSS parse never spans more than one thread (the only `await`
   in the parsing path, `TextSource.PrefetchAllAsync`, completes before tokenizing starts), and a
   rented `StringBuilder`/`SelectorConstructor`/`ValueBuilder` is pure scratch space with no reason to
   cross threads - so the lock was pure overhead. Replaced with `[ThreadStatic]` stacks (zero
   synchronization, not just cheaper synchronization). Re-profiling confirmed `Monitor.Enter_Slowpath`
   dropped to 2.4% of self-time - most of the freed time reappeared as GC/allocation-attributed cost
   rather than vanishing, which is what pointed at allocation volume as the next thing to chase rather
   than more lock-shaped fixes.

3. **`LexerBase.RewindTo` never cleared `_columns`** (the `Stack<ushort>` that lets `Back()` restore
   the previous line's column count across a newline). `RewindTo` is how `StylesheetComposer`'s
   CSS-Nesting classification look-ahead (`IsNestedRuleAhead`) abandons a scan once it decides a
   construct is a declaration, not a nested rule - called once per declaration/nested-rule candidate,
   i.e. on nearly every statement in every stylesheet. Every abandoned scan's pushes stayed on the
   stack forever (nothing pops them, since `RewindTo` repositions the raw source index directly rather
   than calling `Back()`), so a document with many declarations grew this stack without bound over the
   whole parse - and, independently, left stale entries a *later* real `Back()` could pop instead of
   the documented "fall back to 1" behavior. Fixed by clearing `_columns` in `RewindTo`. (Diagnosed
   with a temporary per-call `Console.Error` print gated on `_columns.Count > 500`, which found *zero*
   hits across the full corpus - ruling this out as the dominant allocation source before moving to
   fix 4, and confirming this fix is a correctness/robustness improvement more than a measured
   contributor on this particular corpus.)

4. **`GridTemplateValueConverter.FromCssText` and `CssValueParser`'s `TryGetCalcFunction` both
   tokenized a value through the full CSS-OM `Lexer`/`TextSource` pipeline before checking whether it
   was even shaped like what they were looking for.** `FromCssText` is the one exception the same
   day's `CascadeApplyStyles` defaulting-skip couldn't bypass (`grid-template-columns`/`-rows`'s
   default needs to *parse* `"none"` into a `GridTemplate`, not just default it) - so it ran on every
   box in every document, tokenizing the literal string `"none"` every time. `TryGetCalcFunction`
   backs `IsValidLength`/`ParseLength`/`GetUnit`, called for essentially every length value resolved
   during layout, and unconditionally tokenized just to check `tokens is [FunctionToken fn] &&
   CalcParser.IsCalcFamily(fn.Data)` - true for a tiny minority of real-world lengths ("10px", "50%",
   "auto" never qualify). A caller-sampling diagnostic (temporary: sample 1-in-100
   `Pool.NewStringBuilder()` calls, capture the direct caller via `StackTrace`) found
   `CssValueParser`'s length/color/calc helpers as the dominant callers, confirming this was the real
   target, not the grid-template fix (which only accounted for ~130K of the pool's 2.4M total calls).
   Fixed both with a cheap, exact pre-check before tokenizing: `FromCssText` special-cases the literal
   `"none"` keyword (matching `Convert()`'s already-existing tokenized `isNone` check, and provably
   producing the same `CssProperty` `GridTrackListGrammar.TryParse` would have for a bare `none`
   token); `TryGetCalcFunction` short-circuits on `!length.Contains('(')` - a CSS function token can
   only ever be produced from an ident immediately followed by `(` (CSS Syntax 3 §4.3.4), so this is a
   necessary precondition, not an approximation.

## What was found by running it, not by reading it

**The Pool lock removal's 34-percentage-point CPU self-time drop barely moved wall clock on its
own** (~9.4% combined with fix 1, versus fix 1 alone at ~9.8%) - a `dotnet-trace` CPU sampling
profiler attributes time to whatever frame is on the stack when a sample lands, and GC-driven thread
suspension can land on *any* safepoint-adjacent frame, not just the one doing "real" work. Removing
the lock didn't reduce the underlying GC pressure; it just moved where that pressure showed up in the
profile (`AllocateNewArrayWorker`'s attributed self-time grew roughly as much as `Monitor.Enter_Slowpath`'s
shrank). This is why fix 4's allocation-volume profile (`GC.AllocationTick`-based, not CPU-sample-based)
was the tool that actually found the dominant remaining cost, and why the write-up above leans on
GC-verbose allocation-by-type profiling rather than CPU self-time for fix 4's evidence.

**Fix 4's actual scale only became visible through call-site instrumentation, not the CPU or GC
profiles alone.** Both profiles pointed at `Pool.NewStringBuilder()`/`Char[]`/`Stack<ushort>` as the
largest allocation sources, but neither said *who* was calling it 2.4 million times. A temporary
counter (reverted before landing) found the true total; a temporary 1-in-100 caller-stack sample
(also reverted) found `CssValueParser`'s value-validation helpers, not the grid-template converter
fix 4 started with, as the dominant caller - the grid-template fix alone reduced the total call count
by only ~130K of 2.4M, confirmed by re-running the same counter before and after.

## Deliberately not done

**Line/column tracking's stale-value exposure (fix 3's secondary correctness note) isn't covered by a
dedicated regression test.** `_columns` is a `private` field on an `internal` type with no public
surface to observe the fallback-to-`1` behavior without either exposing internal state for testing or
constructing an adversarial fixture (rewind-then-real-backtrack-over-a-newline) that doesn't correspond
to any known real CSS input. The fix is covered indirectly - every existing CSS-nesting test exercises
`RewindTo` via ordinary declarations - but the specific stale-pop scenario the doc comment describes is
not independently pinned. Left as a documented risk rather than a synthetic test that wouldn't
resemble anything the parser actually sees.

**`PdfGenerator.cs:245`'s `MinContentWidth` clamp branch (`candidatePixelsPerPoint = minPixelsPerPoint`)
is not covered by the diff-coverage gate** (95.2%, one line short of the file's other 100%s; 97%
overall, comfortably above the 90% gate). Three attempts to trigger it through the real
`PdfGenerator.GeneratePdf` API - plain text, a table, and an explicit narrow `<body>` width - all found
the document root's measured `ActualSize.Width` matches the page's available width regardless of
content, so the "content measures narrower than `MinContentWidth`" case this clamp exists for could not
be reproduced through ordinary top-level document layout in the time available. The clamp's logic is
unchanged from `origin/main` (only the local variable it assigns was renamed as part of restructuring
the surrounding block), so this is a pre-existing coverage gap surfaced by the rename, not a new risk
introduced by this change.

## Evidence

Full net8.0 suite: 7025 passed / 0 failed / 9 skipped (up from 7011 - 14 new tests: `NeedsRescale` unit
tests, `PdfGeneratorLayoutHarness.LayoutWithRescaleAsync`-based ShrinkToFit/ScaleToPageSize behavior
tests, and `CssValueParser.IsCalcFunction` fast-path tests). Zero-warning `dotnet build PeachPDF.slnx
-t:Rebuild`. Diff coverage 97% (gate is 90%) against `origin/main`.

Full 73-showcase `PeachPDF.TestHarness` run, Release/net10.0, mean of 6 interleaved warm runs (baseline
and fixed binaries alternated per iteration to cancel out drift), measured against current `origin/main`:

| Metric | main | All four fixes | Delta |
|---|---|---|---|
| Wall clock (mean of 6 interleaved runs) | 19.73s | 14.99s | **-24.0%** |
| Total tracked allocation (`GC.AllocationTick`, one run) | ~22.5 GB (post fixes 1-3) | ~13.0 GB | **-42%** |
| `System.Char[]` allocation | 11.7 GB | 6.3 GB | -46% |
| `Stack<ushort>` allocation | 6.4 GB | 3.0 GB | -53% |
| `Monitor.Enter_Slowpath` CPU self-time | 37.0% (main) | 2.4% (after fix 2 alone) | -34.6pp |

All 73 PDFs still render (page counts, text content, and text extraction/character positions
unchanged for the sampled showcases checked - `multicol`, `font_palette`, `table_header_repeat`,
`invoice` - after normalizing the documented volatile bytes: `/CreationDate`, `/M`, `/ID`, font-subset
tags, and PDFsharp's plaintext header timestamps, per
`.claude/invariants/testing-a-pdf-carries-two-timestamps-not-one-when-showcases-are-compared.md`).
