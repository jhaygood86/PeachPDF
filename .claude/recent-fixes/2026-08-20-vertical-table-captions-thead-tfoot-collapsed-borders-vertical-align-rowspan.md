## Vertical-writing-mode tables: captions, `<thead>`/`<tfoot>` placement, collapsed borders, `vertical-align`-in-cell, and rowspan row-axis sizing (#762)

Closed the remaining sub-areas of #762 beyond the sizing/placement work two earlier PRs already landed for
*simple* vertical tables (see [.claude/accepted-gaps/no-vertical-writing-mode-layout.md](../accepted-gaps/no-vertical-writing-mode-layout.md)).
Each sub-area turned out to be the same shape of bug: code written once for `horizontal-tb` that reads or
writes a physical property (`ActualRight`, `ActualBottom`, `Location.X/Y`, `Border.Top/Right/Bottom/Left`)
unconditionally, never branching on `_isVertical`/`_rowAxisStartIsAtMax`. The fix in every case was to
extend `CssLayoutEngineTable`'s existing "lay out forward as if `vertical-lr`, then mirror once via
`ReflectRowAxisForVerticalRl` once the table's own final row-axis bounds are known" convention, not to
invent a second approach per sub-area.

**Load-bearing idea**: `row.ActualRight`/`row.ActualBottom` bookkeeping at the end of each row-loop
iteration is genuinely axis-dependent, not just axis-labeled — for `horizontal-tb`, `ActualRight` is the
column axis (stable, safe to read via `Boxes.Max` at any point) and `ActualBottom` is the row axis (must
come from `cursor.MaxBottom`, since an open rowspan cell can still grow it). For a vertical table the axis
roles swap, but more importantly a naive swap of *which field* is read from `cursor.MaxBottom` vs.
`Boxes.Max` still isn't enough for a rowspan cell specifically: a rowspan cell's real row-axis footprint
spans multiple rows, so reflecting it only via its opening row's own `OffsetLeft` cascade (the natural
result of extending the existing per-row reflection loop) applies the *opening row's* delta to a cell whose
own footprint differs whenever `rowSpan > 1`. This needed a second, explicit residual-correction pass in
`ReflectRowAxisForVerticalRl`: snapshot each real rowspan cell's own pre-reflection footprint and its
opening row's pre-reflection footprint, run the existing per-row loop unchanged, then apply
`cellDelta - rowDelta` as one more `OffsetLeft` per rowspan cell. `OffsetLeft` is additive, so this
composes correctly with the per-row pass rather than needing to replace it.

**Found only by running it, not by reading it**:
- A `CssSpacingBox` (the rowspan placeholder inserted into rows a span continues through) "never inherits
  style" by its own design, so its `WritingMode` reads as CSS-initial `horizontal-tb` even inside a vertical
  table — its own `PerformLayout` then collapsed the row-axis extent the table engine had just assigned it,
  because plain block layout resolves auto-height from (empty) content. Only visible as a blank, wrongly-
  positioned cell in a rasterized PDF; a `CssBox`-property-only test wouldn't have caught it, since the
  corruption happened *inside* `PerformLayout`, between the table engine's assignment and its later read.
- A long stray border line spanning almost the full page width in the rasterized showcase, traced to
  `GetGridLineY` reading `TableGrid.RowAt(...)`, which references the *detached* `<thead>`/`<tfoot>` source
  row directly — not its `CssProxyBox`'s translated snapshot. `ReflectRowAxisForVerticalRl`'s sweep only
  included the proxies, so the detached row's own geometry was never mirrored; the collapsed-border code
  read it anyway. Fixed by adding `_headerBox`/`_footerBox` themselves to the reflection sweep, separately
  from their proxies.
- A post-change review pass (this repo's own convention, not incidental) found a companion bug beyond the
  above: `cursor.MaxRight`/`MaxBottom` — the trackers a table's own final dimensions settle from — were
  grown from a header/footer proxy's physical `ActualRight`/`ActualBottom` unconditionally at six call
  sites, never axis-swapped. Only one is reachable by a vertical table today (Step 5's closing-footer arm),
  but on that one, a `<tfoot>`'s row-axis extent was left out of the table's own final size, positioning the
  footer outside the table's own settled bounds. Fixed by routing every site through one shared, axis-aware
  helper (`GrowMaxRightFor`) rather than five independent unconditional reads, so future call sites can't
  reintroduce the same omission silently.
- A second post-change review pass (8 parallel finder agents, one per angle, plus a targeted verification
  agent that constructed and ran an actual repro) confirmed a more serious latent bug in code this diff
  never touched: `SettleWhetherTheGroupsRepeat` decides whether a `<thead>`/`<tfoot>` repeats per page by
  comparing `_headerHeight`/`_footerHeight` against a quarter of the *document's* real page-sheet height,
  with no `_isVertical` guard — unlike every sibling page-break check in the same file, which all
  explicitly test `pageHeight >= double.MaxValue - 1` (the sentinel a vertical table's own row loop is
  forced to, since it's placed as one monolithic unit rather than paginated per-row). For a vertical
  table, `_headerHeight`/`_footerHeight` are row-axis (physical-X) quantities, not the physical-Y thickness
  the comparison assumes, so a vertical table with a repeating `<thead>`/`<tfoot>` inside an ordinarily
  paginated document could still trip `_headerRepeats`/`_footerRepeats` true by coincidence of magnitude.
  That let `SliceARowAcrossTheBandsItOverflows` (also unguarded) run its physical-Y page-band arithmetic
  against the table's row-axis cursor, overwriting it with page-band-derived garbage — the verification
  agent's repro showed a row's row-axis thickness balloon from an expected ~22pt to 544pt, a 25x blowup,
  from nothing more exotic than a `<thead>` on a table that happened to span more than one page's worth of
  content. A comment already in this diff (`AssignRowActualBounds`) incorrectly asserted slicing was
  "gated off entirely for vertical tables" — true only of the *value* the `_isVertical` branch reads, not
  of the side effect (`cursor.MaxBottom` mutation) the slicing method itself has regardless of who reads
  its return value afterward; that false belief is almost certainly why no guard was added upstream in the
  first place. Fixed at the single point of truth rather than patching each unguarded consumer separately:
  `_headerRepeats`/`_footerRepeats` are now unconditionally `false` for `_isVertical` tables, which makes
  every downstream consumer (`RepeatedHeaderRoom`/`RepeatedFooterHeight`, and everything gated on them —
  `SliceARowAcrossTheBandsItOverflows`, `RepeatTheGroupsOnEveryBandTheTableSpans`, `RoomForARowIn`) a
  correct no-op without an `_isVertical` check re-added at each one. A regression test
  (`VerticalTable_WithRepeatingThead_ManyRows_ExceedingOnePagesBand_LaysOutWithoutSpuriousInternalPageBreak`)
  sits right next to the pre-existing no-`<thead>` sibling test for the same bug class in
  `TableWritingModeIntegrationTests.cs`, whose own comment already documented the identical physical-X-vs-
  physical-Y mismatch having been found and fixed once before at a different call site — this is the same
  bug shape recurring at a call site that one fix pass missed, not a new kind of bug.

**Deliberately not done**: real per-row pagination of a vertical table's own content — the page-
fragmentation system (`FragmentainerContext`'s `(Top, Bottom)` band, `PageTopOf`/`PageBottomOf`) is
physical-Y-only at the type level, the same wall already documented for Multi-column (#764); tracked
separately as [#783](https://github.com/jhaygood86/PeachPDF/issues/783). A multi-row `<thead>`/`<tfoot>`
group (two or more rows) doesn't yet reverse its own internal row order for `vertical-rl` the way `<tbody>`
rows do, since a real fix needs the group's `CssProxyBox`/`BoxGeometrySnapshot` to individually re-order
captured rows rather than uniformly translate them — tracked as
[#784](https://github.com/jhaygood86/PeachPDF/issues/784); single-row groups (the common case, and the only
shape exercised by the new tests/showcase) are unaffected.

**Evidence**: `CssBox`-property assertions on rowspan combined extent (both `vertical-rl`/`vertical-lr`),
sibling non-corruption, a rowspan cell inside a short-page-height paginated container, rowspan+colspan
combined on one cell, caption row-axis stacking, `<thead>`/`<tfoot>` proxy position (including a regression
test asserting the footer proxy stays within the table's own final bounds), a repeating-`<thead>`-inside-a-
real-paginated-document case (the `SliceARowAcrossTheBandsItOverflows` fix above), `vertical-align`-in-cell
offset; structural assertions on `CssBox.CollapsedBorderSegments` confirming row-boundary segments come out
column-axis-tall/row-axis-thin for a vertical table (the literal inverse of `horizontal-tb`'s own shape);
full `dotnet test --framework net8.0` suite green (9043/9052); zero `dotnet build PeachPDF.slnx -t:Rebuild`
warnings; 100% diff coverage; PDFium+MuPDF rasterization of a combined caption+`<thead>`+`<tfoot>`+
collapsed-border+rowspan `vertical-rl` table (`writing_mode` showcase section 7b) agreeing byte-for-byte.
