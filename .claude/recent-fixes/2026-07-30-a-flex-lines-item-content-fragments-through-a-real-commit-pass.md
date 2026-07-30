# A flex line's item content fragments through a real commit pass

_Landed 2026-07-30._

[#430](https://github.com/jhaygood86/PeachPDF/issues/430): a `column-count` container nested inside a
`display:flex` item lost 72% of its content (176 of 640 words) versus the top-level control case. The
chain the issue itself traced (legacy `CssRect.BreakPage()` relocating a word against page-grid math
inside a column) turned out to be **stale against current `main`** — instrumenting
`SuppressWordPageBreaks` over the issue's own fixture found it `True` for every one of ~700 word-flow
decisions, so `BreakPage()` never executes for this shape at all; that path was dead code by the time
this landed. The real mechanism: `CssLayoutEngineColumns.FillColumns` computes `pageBudget` from real
page-grid arithmetic regardless of whether the flex item's own layout scope is genuinely fragmenting,
correctly produces a `PendingBreakToken` once content exceeds that budget — and
`CssLayoutEngineFlex`/`CssLayoutEngineGrid` never read an item's `PendingBreakToken` after its
measurement layout returns. The resumption record was right; nothing ever asked for it.

## What landed

`CssLayoutEngineFlex` gained a **commit pass** (Phase 9c, `CommitItemContent`), the flex analogue of
the table-cell resumption PR #488 built (`TableRowCursor`/`TableBreakToken`). Grounded in css-break-3
§2.1's own text: the spec's example of a "parallel flow" is literally "the contents of each flex item
in a flex layout row", the same relationship css-tables-3 §6.1 gives a table row's cells — so this
mirrors `TableBreakToken` field-for-field rather than inventing a new shape. Once a line's `Location`
is final (Phase 9/9a — unchanged, still translate-based, so the flex sizing algorithm's own
measurements aren't re-perturbed), a genuinely paginating single-line row/row-reverse container
re-lays-out each item for real: `PerformLayoutBlockifiedAtFinalPosition` runs `box.PerformLayout(g)`
**without** the detach/suppress bracket `PerformLayoutBlockified` uses for measurement, so breaking is
live. A new `FlexBreakToken` (`Box`, `ResumeSlotIndex`, `UnfinishedItems: IReadOnlyList<
UnfinishedFlexItem(Item, Token)>`, `FinishedItems`) carries per-item resumption across fragmentainers,
with contents-based `Equals`/`GetHashCode` from the start — `TableBreakToken`'s reference-equality bug
(a 100,000-pass spin before it was fixed) is a known, avoidable defect class, not something to
rediscover.

`CssBox.PositionAssignedByEngine` (new) marks a box mid-commit-pass so three generic mechanisms that
otherwise assume they own placement/sizing stand down — see
[the invariant](../invariants/fragmentation-an-engine-assigned-geometry-is-not-the-frames-to-re-derive.md)
for what each one corrupted before the flag existed.

## What was tried and measured to fail, before this design

A prototype that simply removed `pageBudget`'s cap when the flex item's scope wasn't fragmenting
turned the 72%-loss into **672/640 words across 17 phantom fragmentainers** — a column grown taller
than any real page slot started overlapping multiple bands, and the existing §4.3 monolithic-slicing
machinery (built for genuinely unbreakable content) drew the same content once per overlapping band.
There is no narrow, pre-commit-pass fix at either the `CssRect` or `CssLayoutEngineColumns` layer;
every shallow patch attempted either did nothing (the dead `BreakPage()` guard) or traded one defect
for another (unbounded budget → duplication). This matches the project's own prior findings on #435
and the #390-stage-4 flex/grid position flip attempt: absolute-Y/page-grid arithmetic does not compose
safely with a genuinely detached scope without the scope itself becoming a real, resumable
fragmentainer.

## Three bugs found only by running it, not by design review

1. **`CssBox.PlaceBlockChild`** overwrote the engine-assigned `Location` on every commit-pass
   re-layout (41 test failures on first wiring) — fixed by gating `LayoutContents`'s placement branch
   on `PositionAssignedByEngine`.
2. **`PerformLayoutEpilogue`'s §4.3 correctors** (keep-with-next retry, avoid/monolithic relocation,
   orphans/widows) re-fired during the commit pass and moved a line
   `RelocateLinesAcrossFragmentainers` had already placed — fixed with `&& !PositionAssignedByEngine`
   guards on all three blocks.
3. **`ResolveOwnInlineSize`'s `GetBoxWidth`** has a `box.Words.Count > 0` branch that sums *stale*
   word widths from the measurement pass and uses that sum in place of the already-pinned `Width` —
   corrupting word-wrap specifically for nested flex-in-flex content (`.row` > `.cell` > `.box` >
   text). Found via a full before/after showcase-corpus diff: `aspect_ratio.pdf` and `box_shadow.pdf`
   changed non-trivially before this fix, became byte-identical to baseline after it. Fixed by
   skipping the `ResolveOwnInlineSize` call entirely for a `PositionAssignedByEngine` box.

## What was measured

Re-running the issue's own fixture (40 paragraphs, `columns:2`, 260pt band) after the fix:
`display:flex` nesting now emits **640/640** words, matching the top-level control case exactly —
same page count, same word span. Confirmed with two-renderer rasterization (PDFium + MuPDF) of a
control/flex-nested pair: every page's PNG is byte-identical between the two renders.

Full net8.0 suite green. 100% diff coverage on the changed lines
(`CssBox.cs`, `CssLayoutEngineFlex.cs`, `FlexBreakToken.cs`). `dotnet build PeachPDF.slnx -t:Rebuild`
zero warnings. 69/69-showcase-corpus (now 73, with showcases added since) diff: 70 byte-identical
after metadata normalization; 3 (`charts_css.pdf`, `flexbox.pdf`, `print_catalog.pdf`) differ by
sub-point, benign border/anti-aliasing shifts, confirmed via amplified-diff visualization and
cross-renderer agreement to be a cosmetic side effect of `Width`/`Height` now round-tripping through
`FormatLayoutUnits`'s 4-decimal string precision on a re-layout that didn't happen before — the same
class of diff this repo already accepted for PR #488's `multicol` showcase.

## What this superseded

`FlexReplacedElementPageBreakIntegrationTests`'s
`ItemAtDefaultAlignment_TextStraddlingPageBoundary_...` pinned the *old* accepted-gap behaviour (a
single item's straddling word staying at the item's own pre-fix `Location.Y` instead of moving to the
next page). Renamed to `..._MovesToTheNextPage` and rewritten to assert the new, correct behaviour;
the class-level doc comment now says which shape is superseded.

## Deliberately not done

Scoped to `flex-direction: row`/`row-reverse`, single-line-only (`lines.Count == 1`), block-level
(`display: flex`, not `inline-flex`). Multi-line (`flex-wrap`) containers, `column`/`column-reverse`
direction, and grid items are unaffected and still lose content the same way #430 originally
described — recorded in
[flex-multiline-item-content-fragmentation.md](../accepted-gaps/flex-multiline-item-content-fragmentation.md)
and tracked as [issue #526](https://github.com/jhaygood86/PeachPDF/issues/526), which carries a
starting design for each.

#400(b)'s conversion of `WouldStraddleFragmentainer`'s page arm to fragmentainer-relative math (the
originally-planned prerequisite) turned out not to be on the critical path: the commit pass only asks
fragmentation questions once an item's position is already final and meaningful, which is exactly the
case #400(b)'s own concern ("this box's absolute Y is not yet meaningful") doesn't apply to. Left
undone on this branch, still real, open work — see #400/#435.
