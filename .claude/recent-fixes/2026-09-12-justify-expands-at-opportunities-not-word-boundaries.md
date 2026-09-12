# `text-align: justify` expands at justification opportunities, not at every word boundary (#1013)

Closes the accepted gap `justify-expands-at-every-word-boundary.md`, filed during the #1011 review.
User-visible consequences are in
[../migration-notes/2026-09-12-justify-expands-only-at-justification-opportunities.md](../migration-notes/2026-09-12-justify-expands-only-at-justification-opportunities.md);
this is the reasoning.

## Load-bearing idea

`ApplyJustifyAlignment` used to **re-walk the line from its start edge**, placing each word at a
running `currentX` and adding a flat `(availWidth - textSum) / wordCount` after every one. Three
separate defects came out of that one shape, and all three disappear together once the method stops
re-deriving positions and instead **shifts each word by the expansion accumulated so far** — the same
progressive-shift approach `ApplyLeaderFill` already uses:

1. A flat per-word share opens a gap where the source has none (`A<span>B</span>`, text beside an
   `<img>`), which is the filed bug.
2. Dividing by the *word* count rather than the *gap* count makes every gap one share too narrow, and
   the unconditional "flush the last word to the end edge" step then dumps the whole shortfall into
   the final gap — it measured exactly 2× the others.
3. Re-deriving positions throws away every natural advance the flow computed. Keeping them is what
   makes css-text-3 §6.4.1's "space distributed by justification is *in addition to* the spacing
   defined by letter-spacing or word-spacing" true by construction rather than by re-implementation,
   and it is also what preserves a float-narrowed line start and an inline box's own padding.

The opportunity model is css-text-3 §6.4.5's minimum requirements for `text-justify: auto` (the only
method PeachPDF implements): **word separators, plus the boundary between a block-script character and
any other character**. Block scripts are approximated by `CommonUtils.IsAsianCharacter` — deliberately
the same predicate `CssBox.ParseToWords` uses to split CJK text one character per word, so the set of
opportunities is exactly the set of word boundaries that split produces, and no others.

## The trap: one third of the word separators belong to no word

The obvious implementation — `previous.HasSpaceAfter || word.HasSpaceBefore` — is wrong on
`<span>AA</span> <span>BB</span>`, which is about as ordinary as markup gets. `ParseToWords` emits
**no word at all** for a collapsible-whitespace-only text node, so neither `AA` nor `BB` carries a
flag; `FlowBox` instead advances the cursor by `box.ActualWordSpacing` for the whitespace-only box
itself. Found by dumping `CssLineBox.Words` for the fixture before writing any code, not by reading
`CssRect` — both flags read `false` while the natural gap was a full 6.598pt.

So the separator is recorded where all three of its sources are visible at once: `CssRect`
`PrecededByWordSeparator`, assigned by the flow at the moment a word is positioned, from
`coordinates.PendingWordSeparator || word.HasSpaceBefore` (and the pending flag then reset to
`word.HasSpaceAfter`). Assigned, never accumulated, so a repeated layout pass over the same box tree
re-derives it instead of compounding it.

The alternative considered and rejected: infer an opportunity from the natural gap being positive
(`word.Left - previous.Right > 0`), which needs no new state at all because justification runs before
anything else moves the words. It was rejected because an inline box's own padding/border also
produces a positive natural gap and is *not* a justification opportunity, so the inference would have
swapped one class of false positive for a smaller one.

## What running it (not just reading it) confirmed

Every expectation was measured in Chromium first, through the repo's existing Playwright dependency
(`BoundingBoxAsync` over one span per word), at `font: 16px monospace` in a 200pt block:

| fixture | Chromium | PeachPDF before | PeachPDF after |
|---|---|---|---|
| `A<span>B</span>` gap on a justified line | 0 | 6.186pt | 0 |
| the nine inter-word gaps on that line | all 10.094px | eight at 6.186pt, last at 12.372pt | all 7.561pt |
| `<span>AA</span> <span>BB</span>` | expands like any separator | expanded (by accident — every boundary did) | expands, via `PrecededByWordSeparator` |
| lone overflowing word, justified non-last line | `x = 0` (start edge) | flushed to the end edge | start edge |
| CJK `一二三…`, 150pt measure | each advance +0.727px | expanded (by accident) | each gap +0.545pt |
| `一二AB三四 CD 五六…` | 0.297px at *both* a CJK boundary and on top of a space | — | 0.241pt at both |

The mixed CJK/Latin row is the one that settled the design: Chromium gives a word separator and a CJK
letter boundary **the same** expansion, which is §6.4.1's equal-priority rule, and is why a single
count and a single share are enough — no priority levels needed.

## What the spec text (not the assumption) changed

Reading css-text-3 in full rather than trusting the §7.3 citations already in the file turned up two
rules the old code contradicted, and both are now load-bearing:

- **§6.4.3 Unexpandable Text** — a line with no justification opportunity aligns as `text-align-last`,
  whose initial `auto` under `justify` is start. A lone word is exactly that line.
- **§6.1** — "If (after justification, if any) the inline contents of a line box are too long to fit
  within it, then the contents are start-aligned: any content that doesn't fit overflows the line
  box's end edge."

Both make an overflowing justified line a **no-op**, which is why the whole `overflowsLine` floor
machinery from #840/#843 is gone rather than ported: a line that is already too long is left exactly
where the flow put it. The multi-word overflow case #840 fixed ends up at the identical position it
had before (its floor-at-natural-spacing walk reproduced natural placement anyway), so
`Justify_MultiWordOverflowingLine_WordsStayInOrder_NoOverlap` and its vertical twin still pass
untouched; only the two *single*-word overflow tests changed, and they changed to what Chromium does.

Section numbers in the touched comments/docs were corrected from §7.1/§7.3 to §6.1/§6.4.x — the TR's
current numbering. `MarginBoxRenderer`'s own §7.1 citation is left alone as out of scope.

## The vertical counterpart is not a mechanical port

`ApplyVerticalJustifyAlignment` shares the opportunity predicate and the share arithmetic, but it
**anchors the column at its inline-start edge before distributing**, which horizontal deliberately
does not. An auto-height vertical box lays its words out against a placeholder far edge and only
learns the real one afterwards (`ApplyVerticalFlushAlignment`'s own remarks, issue #797), so a pure
progressive shift would justify the column in the wrong place entirely. Horizontal needs no anchor
because the flow always leaves a line flush against the edge it started from — and *not* anchoring is
what preserves a float-narrowed line start there.

## What the showcases showed, which no assertion had

All 114 showcases were re-rendered and pixel-diffed against a baseline built from stashed code
(stash only the three `src/PeachPDF` files — the test-side edits must stay, and `docs/`/`.claude/`
never affect a render). **Exactly 11 changed, and all 11 declare `text-align: justify`**; nothing
without it moved. Two caveats for whoever repeats this:

- **Compare pixels, not bytes.** Every one of the 114 PDFs differs byte-wise between two runs — the
  font subset prefix tag (`/DFAAFK+Arial` vs `/KDDEAV+Arial`) is regenerated per run.
- **Threshold the pixel diff at ~16/255.** A dozen files show a few thousand differing pixels whose
  largest channel delta is 6; `custom_properties` does it with a *byte-identical content stream*, so
  it is sub-pixel rasterization noise from those same re-tagged font subsets, not a change.

The two defects this fixed are both plainly visible in the output, which is the argument for looking
rather than only asserting:

- `paged_media_horizontal_reflow`'s justified body text ended **every** line with a doubled gap —
  `sed  do`, `ad  minim`, `ea  commodo`, `right  edge`. Uniform after. That is the divide-by-word-count
  defect, and it was in every justified line the library has ever produced.
- `hyphenation_de_de`'s single-word lines (`Rechtsschutz-`, `Donaudampf-`) sat flushed to the *right*
  edge of a narrow justified column, reading as a stray indent. Flush at the start edge after.
  `writing_mode`'s vertical justified column (`Alpha Beta Gamma Delta Epsilon`) had the same shape on
  the physical-Y axis, with `Epsilon` pushed against the box's far edge; it now starts flush at the
  top. Both are the §6.4.3 unexpandable-text change, and both look like bug fixes to the naked eye.

## Evidence

- New `JustificationOpportunityTests` (10 tests): confirmed to fail against the unfixed library before
  being kept (verified by stashing only the `src/PeachPDF` half of the change — 8 of the 9 that existed
  at that point failed). The one that passed either way was a `word-spacing` additivity test, and it
  could not have worked: a justified line exactly fills its measure, so `word-spacing` provably cannot
  change the final gaps when the same words land on the line. It was replaced with the inline-`<svg>`
  adjacency case the issue also names, which does fail against the unfixed library.
- Full suite `--framework net8.0`: 11 036 passed / 0 failed / 9 skipped.
- `diff-cover` against `origin/main`: **100%** on all 55 changed lines.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors (run in `Release` — a Debug rebuild
  could not write `PeachPDF.SourceGenerators.dll`, which an open IDE had locked).
