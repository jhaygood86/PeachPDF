## Real per-column pagination of a plain-grid vertical table (#783)

Closed the literal reading of the gap this issue's own title described - "real per-row pagination of a
vertical table's own content" - by establishing that reading has no basis in CSS Fragmentation's model at
all, and building the reading that does instead.

**The load-bearing spec finding**: [CSS Fragmentation Module Level 3](https://www.w3.org/TR/css-break-3/)
§2 defines fragmentation direction as the block flow direction of the *fragmentation root* (the page),
explicitly noting a descendant may have a different block flow direction without that changing which axis
fragmentation follows. A vertical table's rows advance along physical X (its own block axis) - but that
was never going to be the axis a page fragments along, regardless of any engineering effort spent on it.
What genuinely *is* the page's real fragmentation axis (physical Y) is the vertical table's own **column**
axis (css-tables-3: columns run along the inline axis, physical Y under `vertical-rl`/`vertical-lr`) - so
the fix targets column-axis pagination, not row-axis, and needs zero changes to `FragmentainerContext`/
`HtmlContainerInt`'s existing Y-based page-grid primitives, unlike the axis-agnostic rewrite the issue's
own text originally speculated might be required.

**Second load-bearing finding, from a fresh exploration pass before writing any code**: a *row*'s own
row-axis extent still cannot be known without laying out that row's cells across every column at once (no
per-row equivalent of `_columnWidths[]`'s own CSS-hint-driven precomputation exists) - so an interleaved
column-major layout loop, paginating column-by-column the way the existing row loop paginates row-by-row,
would need a genuine two-pass architecture (measure every row unpaginated, *then* paginate) to know where
rows even go. Building that was reconsidered against a materially simpler design once a further fact was
confirmed: every cell in one column shares the *exact* same column-axis extent (`cell.ActualBottom =
cell.Location.Y + width` in `LayoutBodyRow`, `width` read straight from `_columnWidths[]`, never from that
cell's own content) - meaning a page boundary either falls cleanly *between* two columns, or cuts through
every cell of one column identically, with no content-driven break point to find *inside* a column's own
fixed-height box either way.

**Approach shipped**: `CssLayoutEngineTable.RelocateColumnsAcrossPageBoundaries` runs once, after the
table's ordinary (still-forward-grown, unpaginated) row loop has already settled every cell's real
geometry - not a new interleaved layout pass, a relocation pass over already-correct geometry. It plans
every column's destination slot first (mutating nothing), and only commits - via `CssBox.OffsetTop` subtree
translation of every row's cell in a relocating column, the same mover pattern `ReflectRowAxisForVerticalRl`
already uses one axis over - once every column is confirmed to fit somewhere. A single column whose own
fixed extent exceeds even a fresh page's whole band cannot be helped by any relocation (there is no content
to slice at a boundary that falls in the middle of a grid-fixed-height box) - discovered partway through,
that would leave earlier columns already translated with no undo, so the whole pass bails out unmodified
the instant that is found, deferring to the table's pre-existing "move the whole table" fallback for that
one sub-case. No new "did this table fragment" fact was needed either: `CssBox
.PaginatedItsOwnContentWithoutBreaking` already answers that generically from `PageBreakBottoms`, exactly
the way an ordinary `horizontal-tb` table's own row-break decision already works - this pass just states
that same fact when it relocates anything, and leaves it unset (falling through to today's monolithic
move-whole behavior with no special-casing) whenever it finds nothing to do or bails out.

**Scoped to a first cut, deliberately**: `MonolithicContent.HasColumnPaginationExcludedFeature` gates the
whole pass on a "plain grid" table - no `rowspan`, `colspan`, `<caption>`, `<thead>`/`<tfoot>`, or
`border-collapse: collapse`. Each of those needs its own real, independent extension (a colspan cell
straddling the paginated axis needs its own pagination-shell mechanism; a rowspan cell's `CssSpacingBox`
placeholders break the relocation pass's one-cell-per-column-per-row assumption; header/footer repetition
means something structurally different once the *column* axis, not the row axis, is what repeats-worthy
content would repeat across; and so on) - tracked as a new, separately-scoped follow-up
([#793](https://github.com/jhaygood86/PeachPDF/issues/793)), keeping #762 open rather than closing it on
the strength of this first cut alone. `MonolithicContent.IsUnresumableVerticalTable` is narrowed to this
same predicate, so #762's own already-shipped, already-tested combined scenario (rowspan+colspan+caption+
thead-tfoot+collapsed-borders together, the `writing_mode` showcase's own section 7b) stays monolithic and
byte-for-byte unchanged.

**Found only by rendering it, not by reading it**: the first showcase attempt used `px` column heights
against an A4 page's own ~757pt content band, and rendered on a single page - `height: 70px` is 52.5pt
(this repo's own 1px = 0.75pt convention), twelve of them (630pt) comfortably fitting one page. Not a bug,
but a reminder that this repo's own painting-verification convention (rasterize and look) catches sizing
mistakes in a *test fixture* just as readily as mistakes in the code under test - resized to `100px`
(900pt total) to actually exercise a page break, then confirmed with both PDFium and MuPDF agreeing
pixel-for-pixel: the table's own outer border and each page's own slice-bottom border (`PageBreakBottoms`,
read by `FragmentPainter`) both paint correctly on every page, not just the geometry underneath them.

**Evidence**: new `CssBox`-property tests in `TableWritingModeIntegrationTests.cs` (`vertical-rl`/
`vertical-lr` siblings for the relocating case, a no-relocation-needed regression guard confirming the pass
is a true no-op when nothing crosses a boundary, and an excluded-feature-with-rowspan case confirming the
whole-table monolithic fallback is unchanged), each confirmed meaningful by reverting the relocation call
and watching the relocating-case tests fail; a new `writing_mode` showcase section (7d), rendered through
both PDFium and MuPDF agreeing pixel-for-pixel; full `dotnet test --framework net8.0` suite green; 100%
diff coverage; zero `dotnet build PeachPDF.slnx -t:Rebuild` warnings.

**Four more real bugs found by a post-change review pass, before any of this reached `main`** - the same
four-angle review this repo runs on every non-trivial change, and worth naming because none of them were
caught by the geometry-only tests the first draft shipped with:

- **The table's own bounds and each row's own bounds were never updated after relocation.**
  `RelocateColumnsAcrossPageBoundaries` moved every relocated cell via `OffsetTop` (which keeps a cell's own
  `ActualBottom` in sync, since it's a computed property), but `_tableBox.ActualBottom` and each row's
  `ActualBottom` are real, stored fields settled by Step 7/`SetRowGroupBoxDimensions` *before* this pass
  runs - left untouched, they still named the table's pre-relocation extent. That is not cosmetic: a
  following sibling is positioned from `_tableBox.ActualBottom` (`CssBox`'s own in-flow child-placement
  read), so the sibling landed on top of the relocated column(s) rather than after them - confirmed with a
  numeric test (`table.ActualBottom=340` against a true `lastCell.ActualBottom=360`, the sibling placed
  20pt into the last column) before the fix, matching exactly on both sides after it. Fixed by growing
  `_tableBox.ActualBottom` by the last column's own planned delta (provably the largest and the one that
  names the table's true trailing edge - see the method's own updated remarks) and recomputing each row's
  `ActualBottom` from its own now-relocated cells, then re-running `SetRowGroupBoxDimensions`. Two
  independent review angles converged on this from different directions (one tracing `ExtentOf`/
  `BoundsEndAtItsContent` in `FragmentEmitter`, the other from "which established sibling movers in this
  same file re-settle a box's own bounds after moving its children") - a good sign it was real, not a single
  reviewer's misreading.
- **A ragged row (fewer cells in one row than row 0) could crash or silently mis-relocate.** The pass reads
  `columnCount` from row 0 and indexes every row positionally by it (`_bodyRows[r].Boxes[c]`) - a "plain
  grid" table (no rowspan/colspan, already excluded) is still not guaranteed to have the same cell count in
  every row, since `InsertEmptyBoxes` never pads a row for anything but a rowspan cell. Fixed with an
  explicit per-row count check that bails out to the existing whole-table mover, the same "plan, then
  commit" bail-out philosophy the method already uses for an unfittable column.
- **A vertical table as a flex/grid item could get stranded mid-page-break.** Before this predicate existed,
  every vertical table was unconditionally monolithic, so `LineRelocation.MayNotBeCut` always moved a flex
  line/grid row holding one to the next fragmentainer as a unit. A plain-grid table answering "no" to that
  question now would let the line straddle a boundary while this table's own relocation pass moves its
  columns with no awareness of the line it sits in - two uncoordinated movers acting on the same content.
  `HasColumnPaginationExcludedFeature` now also excludes a table whose immediate parent is a flex/grid
  container, keeping that combination monolithic exactly as before; real coordination between the two
  mechanisms is deferred to the same follow-up ([#793](https://github.com/jhaygood86/PeachPDF/issues/793))
  as the other excluded features.
- **A resumed (continuation) pass had no guard against re-running relocation.** The pass ran unconditionally
  at the end of every `LayoutBodyRows` call, with row 0 as its reference for every column's position - on a
  continuation pass, row 0 is not fresh, and may already reflect an earlier pass's own relocation. Whether
  recomputing from it a second time was actually safe rested on an unproven, untested invariant. Fixed with
  an explicit `_continuesAPreviousPass` guard: this pass only ever runs on the fresh pass that places row 0.

None of the four needed touching the pass's core algorithm or its scope boundary - each is a real gap the
first draft's own tests didn't reach, closed without changing what the feature does.
