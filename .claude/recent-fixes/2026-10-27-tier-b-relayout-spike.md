# Tier B spike: can layout resume from a page, so a height change does not re-lay out the whole document?

Research only, no product code. The structural findings come from reading the code; the cost model was then **measured** (section "Measured", below), and it changes the recommendation.

## Measured (Release, net8.0, synthetic corpus, one machine, 3 repetitions each)

Method: a temporary Engine.Tests test (deleted) built an `HtmlContainerInt` over `HtmlRenderContext`, timed `SetHtml` (parse + cascade), then called `PerformLayout` three times on the same tree, with GC pause and allocation counters. `LayoutGeneration` showed exactly one `LayoutDocument` per `PerformLayout` in every case.

| document | pages | parse+cascade | layout #1 | layout #2 (same tree) |
| --- | --- | --- | --- | --- |
| 200 paragraphs | 23 | 40-45 ms | 105-120 ms | 100-145 ms |
| 2000 paragraphs | 223 | 65-100 ms | 1.5-1.8 s (631 MB allocated) | 2.8-3.3 s (1192 MB) |
| 1000 paragraphs + inline `strong/em/a` | 109 | 60-80 ms | 220-280 ms | 540-700 ms |
| 400-row table | 24 | 11-22 ms | 40-90 ms | 26-57 ms |
| 1500 list items | 58 | 45-80 ms | 195-215 ms | 250-320 ms |

What it says:
1. **Layout dominates, not parse/cascade** - 3x to 20x. Skipping a re-parse buys 3-15% at most.
2. **Re-running layout on the same tree is not cheaper than the first layout** - 1.0x (tables) to 2.5x (inline-heavy) *slower*, with 1.7-1.9x the allocation, even though word measurement is cached. The "B-lite is cheap because only the fragmentation walk repeats" premise is false: B-lite is a correctness/simplicity win (no reload), not a speed win.
3. **GC pause is only 5-7% of the time** (the memory note about CPU samples misattributing GC pause applies, but here pause is small), while allocation is large and not linear in content: 11 MB at 23 pages vs 631 MB at 223 pages for 10x the text.
4. **Cost depends on page geometry, not content size - root-caused below.** 1000 paragraphs on one tall page: 64 ms, 18 MB. Same text, page height swept: 21-31 MB at 300, 421, 600, 700, 900, 1000 and 3000 pt; **172-182 MB and 4-8x the time at 800 and 842 pt**, and 107 MB at 1500 pt. A deterministic geometry-dependent spike.

## Root cause of the page-height spike (measured with temporary phase timers and counters, since reverted)

Not rewinds (`PassRewinds` was 0 everywhere, `FragmentainerPasses` equal to the page count at 842 pt, `LastResortRelayouts` 0), not GC pause, and not the box layout pass (about 45 ms at every height). Phase timers around `LayoutDocument`'s calls, 1000 paragraphs:

| page height | `Root.PerformLayout` (box pass) | `CatchUpStaleSlotsBehind` | `EmitPass` | `BuildDraft` calls | allocation |
| --- | --- | --- | --- | --- | --- |
| 820 pt | 47 ms | 1 ms | 14 ms | 64,518 | 21 MB |
| 842 pt | 45 ms | **148 ms (77 MB)** | **193 ms (80 MB)** | **337,737** | 172 MB |
| 800 pt | 224 ms | 273 ms (81 MB) | 335 ms (84 MB) | 355,192 | 182 MB |

The chain: with the default `orphans: 2; widows: 2`, at some page heights a paragraph that straddles a page break is relocated (`CssBox.CommitBlockChildOffset` -> `OnBlockAxisRelocated` -> `InvalidateEmittedFragmentsFor`, or `TryRewindForWidows` -> the same call) **after its first lines were already emitted into the previous, frozen slot**. `FragmentEmitter.InvalidateFrom` re-opens that slot (`_stale`), and the next pass calls `CatchUpStaleSlotsBehind`, which re-emits each stale slot with `EmitSlot` - a full, *unpruned* walk of the whole box tree (its own doc comment says so: "~145,000 `BuildDraft` calls apiece on the Icelandic Dictionary"). Here that is about 2,500 `BuildDraft` calls per catch-up on 1000 paragraphs, i.e. the whole document, to rebuild one page. Cost = (affected page breaks) x (document size): **quadratic in length**, and it hits roughly a third of page heights.

Confirmed by removing the trigger: with `p { orphans: 1; widows: 1 }` the same document has **0 invalidations, 0 catch-up emits, flat 21 MB and 67-250 ms** at 800, 842, 900 and 1500 pt (default: 94-351 MB, 211-1834 ms in the same run). Every ordinary document has orphans/widows 2, so this affects normal PDF generation, not just an editor.

**Resolved** by `2026-10-27-per-paragraph-reopening-scope.md`: further measurement showed the dominant cost was not the catch-up walks themselves but every reopening retiring the pruning marks of the whole document (one scope under `<html>`), so the fix was finer scoping, not batching. The candidates below were the options before that measurement; keep them only as history.

Candidate fixes (considered before that measurement; all touch the fragmentation emitter and its documented invariants):
1. Prune the catch-up walk by the stale slot's band: skip a box subtree whose recorded geometry lies wholly outside the slot. `VerifyPruningAgainstFullWalk` is the existing oracle for any pruning change; the code comments explain why mid-replay pruning *marks* are unsafe, so this must be geometry-based, not mark-based.
2. Batch the stale slots: one walk that builds every stale slot at once (a single document walk per catch-up call or per `Finish`, instead of one per slot).
3. Avoid the re-open: when orphans/widows relocate a paragraph whose first lines are in a frozen slot, emit the relocation as a continuation fix-up of just that box's fragments rather than un-freezing the whole slot.
Option 2 is the smallest change; option 1 gives the best asymptotics (cost per catch-up proportional to the slot, not the document).

Consequences for tier B:
- For one-page content (a chat message: tens of ms even at 100 paragraphs), a full re-layout on every edit is fine, so B-lite is enough for PeachChat-style use.
- For editor-sized documents (100+ pages), a full re-layout is 0.3-3 s per change, so resume-from-page-k (which skips earlier pages' work, rewind churn included) is the only tier that scales.
- The page-height pathology is worth fixing on its own: it makes every large PDF render several times slower at common page sizes. Tracked as a separate investigation, not part of this change.
- A trap the harness documents (`LayoutHarness.LayoutRepeatedlyAsync`): re-laying a table with a repeating `<thead>` over the same tree measures taller, because per-page proxies are never removed. My table corpus had no `thead`, so the "same height" check passed; B-lite must be checked against that case.

**Answer.** Two different things were being called "incremental relayout":
- **B-lite** - re-run the existing layout driver on the same, mutated box tree. Feasible, and an existing precedent: `RunLayoutToSettledMeasure`, the footnote/page-float loop and the `target-counter(page)` loop each reset `Root.Size/Location/ActualSize` and call `LayoutDocument` again on the same tree, 3 to 7 times per document today. Re-measurement is skipped for unchanged text (`MeasureWordsSize` returns early on `_wordsSizeMeasured`, cleared only by `ParseToWords`), so what is repeated is the fragmentation walk, not parse/cascade/measure.
- **Resume from page k** - plausible, not free. The resume record is already a value (`BreakToken` chain + `(slot, token)` entries in `_passEntries`), and `TryRewindForRunPull` / the widows rewind already restart a pass from an earlier slot via `FragmentEmitter.InvalidateFrom` + `PassRewind.RollBackTo`. What is missing is a snapshot of the state `LayoutDocument` throws away at the start (a fresh `FragmentEmitter`, `PageGeometry.Reset`, named-page registrations, blank-slot reservations, footnote budgets, `ActivePageName`). Geometry lives on the boxes and is mutated in place, which is why earlier pages stay valid only if their boxes are left alone.

**Decision (revised by the measurements).** Layout does dominate, but re-running it is not cheaper, so: build the mutation API first (it is needed by every tier), ship B-lite as the simple tier for small documents, and plan resume-from-page-k for large ones rather than deferring it indefinitely. Investigate the page-height cost spike independently, since fixing it helps every tier and every PDF.

**Document-global features that force a full relayout (refuse in a first cut):** footnotes and page floats, `target-counter(_, page)`, `UseVariableInlineMeasure` (per-page width overrides), named pages, `float-reference`, `@container`, and anything with `counter(pages)` in a margin box (a page-count change reshapes every margin box; `LayoutMarginBoxes` runs after the tail). Orphans/widows/keep-with-next reach *backward* one page, so a resume point must be min(page of the edited box, page of the head of any keep run reaching it).

**The real prerequisite is a box-tree mutation API, not the relayout.** Text is `CssBox.ParseToWords` -> `AppendWordsFromText` plus `DomParser.CorrectTextBoxes`; bidi levels come from `CssBidiParagraphResolver.AssignBidiLevels`, which resolves per *paragraph* across inline siblings and ancestors, so `SetText` must re-run it for the whole enclosing paragraph, split per-codepoint fonts, discard stale `LineBoxes`/rectangles (`RectanglesReset` on the box and its line parents), and clear `_wordsSizeMeasured`. A font-size change needs a re-cascade of descendants (no live inheritance). Insert/split of text nodes needs `CorrectTextBoxes`-equivalent anonymous-box handling. Layout-affecting `SetStyle` stays refused (`UpdateResult.NotApplied`) until this exists.

**Caveat to carry forward.** A re-run of `LayoutDocument` is documented as "observably not a no-op" for some documents (`HtmlContainerInt.cs` near the settled-measure and footnote loops), so B-lite must run exactly the sequence the first layout ran, and the correctness oracle is the differential test from the plan: after each incremental update, compare against a fresh full layout (fragment trees, page counts).

**Recommended order.** (1) profile `LayoutDocument` vs parse/cascade/measure on a corpus (`PeachPDF.TestHarness/Program.cs` records per-run wall times), because B-lite is only worth it if the walk is cheap relative to a full reload; (2) the mutation API (`SetText`, `InsertText`, `SplitTextNode`, `WrapRange`, `RemoveNode`) with a refusal list; (3) B-lite wired to `SetStyle`/mutations and returning dirty regions from the first changed page onward plus the new page count; (4) differential test harness; (5) only then consider resume-from-page-k.
