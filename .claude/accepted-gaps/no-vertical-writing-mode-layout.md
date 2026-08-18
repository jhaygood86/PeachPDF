# `writing-mode` real layout is partial: inline content only, no block children, table/columns unaffected

Tracking issue: [#547](https://github.com/jhaygood86/PeachPDF/issues/547).

CSS Writing Modes Level 4 defines `writing-mode: vertical-rl` / `vertical-lr` / `sideways-rl` /
`sideways-lr` as rotating which axis is inline and which is block for a box's whole layout: line boxes
stack along the block axis, glyphs orient per
[§text-orientation](https://www.w3.org/TR/css-writing-modes-4/#text-orientation), and both
[flex](https://www.w3.org/TR/css-flexbox-1/#writing-mode) and
[table](https://www.w3.org/TR/css-tables-3/) layout reinterpret their main/cross axes accordingly.

## What now works

`writing-mode`/`text-orientation` parse, cascade, and inherit correctly, and `writing-mode` correctly
drives CSS Logical Properties resolution (unchanged from before). Beyond that, a block box whose
`writing-mode` is `vertical-rl`/`vertical-lr` and whose content is inline-only (plain text and simple
nested inline elements — `DomUtils.ContainsInlinesOnly`, no block-level children) now gets real vertical
line flow: `CssBox.LayoutContents` dispatches such a box to
`CssLayoutEngine.CreateVerticalLineBoxes` instead of the ordinary horizontal `CreateLineBoxes`/`FlowBox`.
Lines ("columns") stack along the block axis (right-to-left for `vertical-rl`, left-to-right for
`vertical-lr`, via `WritingModeFrame`'s logical-to-physical conversion), text runs top-to-bottom within
each column, auto height shrinks to the content's own inline-axis extent, and glyphs paint rotated 90°
(`FragmentPainter.Text.cs`'s `SidewaysRotation`, reusing the `RGraphics.PushTransform`/`RMatrix` mechanism
already proven by `SvgRenderer.PaintGlyphs`). Such a box is treated as monolithic with respect to its
parent's own page fragmentation (`MonolithicContent.IsUnresumableOrthogonalFlow`) — it lays out its whole
subtree in one pass and is moved (not sliced) if it doesn't fit the current page, the same way a replaced
element is.

Auto width also shrinks to the content's own block-axis extent now (issue #761), the direct counterpart
of auto height's own inline-axis shrink above: `CreateVerticalLineBoxes` tracks the total block-axis
extent the wrap loop actually used (`blockOffset + lineThickness`, the last line's own thickness folded
in) and, for an auto (non-explicit) `width`, moves the box's block-end edge to match — `Location.X` for
`vertical-rl` (block-start is the right edge, which stays fixed, matching how `ClientTop` stays fixed for
auto height), `ActualRight` for `vertical-lr` (block-start is the left edge, the same shape the height
case already has). `CssBox.ActualRight` is a *written* property whose getter is `Location.X + Size.Width`,
so the `vertical-rl` case has to capture the true (pre-shrink) right edge and re-apply it after moving
`Location.X`, or the getter reports a moved edge Size.Width was never told to give up — verified against
both PDFium and MuPDF rasterization (matching output) and the full test suite. One narrow follow-up:
`width: auto; max-width: Npx; margin: 0 auto` on a vertical box computes centering margins against the
pre-shrink (max-width-clamped) width, and the content-driven shrink afterward doesn't re-center against
the final, smaller width, leaving the box off-center — tracked as
[#773](https://github.com/jhaygood86/PeachPDF/issues/773). Plain `width: auto` (no `max-width`) is
unaffected, since auto margins already resolve to 0 whenever width itself is auto.

Flexbox (`display: flex`/`inline-flex`) is also writing-mode-aware now: `CssLayoutEngineFlex` resolves
which physical axis is its main axis (`_mainAxisIsPhysicalX`) and which physical end main-start/cross-start
land on (`_mainStartIsAtMax`/`_crossStartIsAtMax`) from `LogicalPropertyResolver` — the same abstract-to-
physical table `WritingModeFrame` reuses — rather than assuming `row` always means physical-X. A `row`
container's items stack along the container's own inline axis (physical Y under `vertical-rl`/`vertical-lr`)
and a `column` container's along the block axis (right-to-left for `vertical-rl`, left-to-right for
`vertical-lr`); sizing (`Width`/`Height`, min/max, margins), alignment (`justify-content`'s physical
`left`/`right` fallback, `align-items`/`align-self` stretch and cross-axis positioning), and which of the
two page-fragmentation models applies (row's "parallel flows sharing one physical-Y band" vs. column's
"sequential flow along physical Y") all follow the resolved physical axis rather than the `row`/`column`
keyword. `row-gap`/`column-gap` selection is deliberately unaffected (CSS Flexbox 1's own row/column gap
identity is tied to `flex-direction`, not the physical axis it lands on). Verified against both PDFium and
MuPDF rasterization (byte-identical layout) per this repo's paint-verification convention, and the existing
481 horizontal-tb Flexbox tests all still pass unchanged.

Table (`display: table`) is also writing-mode-aware now for the simple case: `CssLayoutEngineTable`
resolves `_isVertical`/`_rowAxisStartIsAtMax` from `LogicalPropertyResolver.BlockStart` and reinterprets
its column/row-sizing pipeline, cell placement, and final table-dimension settling through that axis
mapping rather than assuming rows always stack along physical Y and columns along physical X. Rows always
stack along the block axis and columns always run along the inline axis per
[css-tables-3](https://www.w3.org/TR/css-tables-3/) — for a vertical table that means rows stack along
physical X (right-to-left for `vertical-rl`, left-to-right for `vertical-lr`) and columns run top-to-bottom
along physical Y, using each cell's own `height` (not `width`) as its column-sizing hint. Cell placement
uses a "layout everything forward, then reflect once" design for `vertical-rl` specifically: the row loop
always places rows growing left-to-right (`vertical-lr`'s own natural shape, since a row's own row-axis
thickness isn't known until after its cells are placed — a chicken-and-egg problem the single-pass
measure-and-place loop can't otherwise resolve), and a table already laid out this way gets one whole-table
mirror pass (`ReflectRowAxisForVerticalRl`) when `vertical-rl` actually wants right-to-left growth. This
also required fixing a real, previously-unknown bug in `CssBox.ResolveOwnInlineSize`, which unconditionally
skipped physical-width resolution for every table cell (correct for `horizontal-tb`, where width is the
table-controlled column axis, but wrong for a vertical cell, where width is the cell's own content-driven
row-axis extent — the same role physical height already plays for an ordinary horizontal cell via
`CssLayoutEngine.ApplyHeight`). A vertical table is treated as monolithic with respect to its parent's own
page fragmentation (`MonolithicContent.IsUnresumableVerticalTable`), the same "moved whole, not sliced"
treatment `IsUnresumableOrthogonalFlow` already gives a vertical inline-only box — the table engine forces
`pageHeight` to `double.MaxValue` for a vertical table's own row loop (routing it through the pre-existing
unpaginated fallback path rather than real per-row pagination), so the outer driver must never try to slice
one mid-row. Getting that guarantee to actually hold took two more fixes beyond the pageHeight override
itself: `WillCrossPageBoundary`/`StraddleCorrectionAppliesTo` (the per-row and straddle-correction checks)
compared this table's own row-axis (physical-X) cursor against the *container's* real physical-Y page
size rather than the table's own `pageHeight` override, so a vertical table with enough rows for its
row-axis extent to exceed one page's band height could still trip a spurious internal break — both now
take `pageHeight` as a parameter and check that instead; and a bottom `<caption>` left `_tableBox
.ActualRight` (the row-axis-max edge `ReflectRowAxisForVerticalRl` mirrors every row against) unset,
corrupting every row's position rather than only the caption's own already-disclosed-as-unconverted one —
Step 7 now settles `ActualRight` in that branch too. A third, pre-existing bug surfaced along the way:
`DetermineMissingColumnWidths`'s auto-width "spread extra width between columns" step could shrink a
column below its own already-computed content width whenever `availCellSpace` (the column axis's
available space, for a vertical table) came out smaller than the columns' combined content size — the
common case for a vertical table with no definite height anywhere up its containing-block chain — instead
of leaving a column at its content-driven width the way an indefinite/auto containing block should;
guarded so the spread only ever grows a column, never shrinks one. Verified with `CssBox`-property
assertions (row/column stacking direction and spacing, column-sizing-from-height and its uniformity across
rows, the page-break and caption regressions above), fragment-level `IsMonolithic`/page-straddle
assertions, and the existing 544 horizontal-tb Table tests all still pass unchanged (the column-width
guard, in particular, only ever removes a shrink no horizontal-tb test relied on). Scoped to simple tables
only — see below for what `display: table` still doesn't handle under a vertical writing mode.

**Real per-character `text-orientation`** ([#765](https://github.com/jhaygood86/PeachPDF/issues/765)) now
works: `mixed` (the default) classifies each codepoint by Unicode's Vertical_Orientation property (UAX
#50 — `U`/`Tu` upright, `R`/`Tr` rotated, `Tu`/`Tr`'s own "transformed" fallback collapsed to plain
upright/rotated since this engine has no vertical-form GSUB substitution) via a compact, Brotli-compressed,
run-length-encoded lookup table (`VerticalOrientationTable`, mirroring the existing `BidiClassTable`
pattern) generated from the real UCD `VerticalOrientation.txt` data file. `CssBox.AddWord`/
`EmitPerCodepointFragments` split a word into maximal same-orientation runs (composing as a third axis
alongside the pre-existing small-caps-case-run and per-codepoint-font-run splits) and tag each fragment
`CssRect.IsUprightOrientation`; `text-orientation: upright`/`sideways` instead force one answer for every
word on the box, skipping the split entirely. `FragmentPainter.Text.cs`'s `PaintUprightVerticalRun` paints
an upright run one character at a time, stacked down the column and centered across it, rather than
rotating the whole run as one natural horizontal glyph run the way the (still-default-for-non-upright-runs)
rotated path does. Finding the right per-character down-the-column *advance* took two attempts: the first
(each character's own individually-measured horizontal advance width) kept layout and paint mutually
consistent with each other, but both were consistently wrong versus what `RGraphics.DrawString` actually
paints — a glyph always renders across the font's full line height (ascent+descent) from its anchor,
independent of that glyph's own hmtx advance, and a real subsetted CJK font's advance width measured
narrower than its line height, so every character visibly overlapped the next and the run's own reserved
extent under-ran into whatever followed. The advance is now the font's own line height instead — the
closest available per-character constant that can never under-advance, given no true vmtx table is
consulted yet (see below). Verified with `VerticalOrientationTableTests` (table lookups against real UCD
data), `TextOrientationIntegrationTests` (word-splitting via a `CssBox`+layout harness, and
`RGraphics`-call-sequence assertions for both the per-character upright paint path and the rotated path via
a `RecordingGraphics` mock — 5 of 10 tests confirmed meaningful by failing when the feature was temporarily
disabled), and both PDFium and MuPDF rasterization of the `writing_mode` showcase's section 8 (mixed CJK
upright next to rotated Latin/digits, upright-forced, and sideways-forced examples, all agreeing between
renderers with no overlapping or garbled glyphs).

## What's still out of scope

- **Block children inside a vertical box** ([#760](https://github.com/jhaygood86/PeachPDF/issues/760)).
  `CreateVerticalLineBoxes` only runs for inline-only content; a vertical-writing-mode box containing a
  nested block element (or itself containing another vertical- or horizontal-writing-mode block child —
  orthogonal flow) still lays out as ordinary `horizontal-tb`.
- **Floats, absolute positioning, hyphenation, bidi reordering, `text-align`, and
  `box-decoration-break: clone`** are not honored inside a vertical box's own content
  ([#768](https://github.com/jhaygood86/PeachPDF/issues/768)).
- **A nested inline element's own border/padding/margin does not reserve inline-axis (physical left/right,
  for `vertical-rl`/`vertical-lr`) space** ([#769](https://github.com/jhaygood86/PeachPDF/issues/769)), and
  its block-axis (physical top/bottom) padding/border is applied once per column it spans rather than once
  total — `CreateVerticalLineBoxes` never sets `CssBox.FirstHostingLineBox`/`LastHostingLineBox`, the
  bookkeeping `CssLineBox.UpdateRectangle` needs to gate leading/trailing inset correctly, so a
  bordered/padded `<span>` inside vertical text paints with the wrong decoration box.
- **Atomic inline-level content (`inline-block`/`inline-table`) is flattened, not treated as one atomic
  unit** ([#771](https://github.com/jhaygood86/PeachPDF/issues/771)). `MeasureAndCollectWordsInDocumentOrder`
  recurses into every descendant box with no formatting-context check, so a nested inline-block's own words
  get placed as ordinary flat words indistinguishable from its surrounding text, and the inline-block box
  itself is never positioned (its own border/padding/background never paint). `<img>` (a genuinely atomic
  replaced element with no nested word-bearing subtree) is unaffected.
- **An explicit `writing-mode` override on a non-atomic nested inline element** (e.g. a `<span>`, not an
  `inline-block`/`inline-table`) is laid out using its containing block's writing-mode (correct — a plain
  inline never establishes its own flow) but may paint using its own, different, cascaded `WritingMode`
  value, producing a rotation mismatch. Per CSS Writing Modes, `writing-mode` has no defined effect on a
  non-atomic inline in the first place, so this is a narrow, spec-consistent-to-ignore edge case rather
  than a behavior authors should rely on either way.
- **Real per-character `text-orientation`** ([#765](https://github.com/jhaygood86/PeachPDF/issues/765)).
  Every glyph currently paints rotated 90° regardless of `text-orientation`'s value (equivalent to
  `sideways` always) — `mixed`'s real per-character upright/rotated split (Unicode's Vertical_Orientation
  property) is not implemented.
- **`sideways-rl`/`sideways-lr`** ([#766](https://github.com/jhaygood86/PeachPDF/issues/766)) still render
  as `horizontal-tb` throughout (`WritingModeFrame.IsVertical` is true only for `vertical-rl`/`vertical-lr`).
- **Table remaining gaps** ([#762](https://github.com/jhaygood86/PeachPDF/issues/762), stays open for
  these): collapsed borders (`CollapsedBorderResolver`'s candidate collection still hardcodes physical
  top/bottom/left/right edge reads), `<thead>`/`<tfoot>` repetition, `<caption>` (beyond the narrow
  `_tableBox.ActualRight` fix above, which only stops a bottom caption from corrupting the *other* rows'
  positions — the caption's own placement stays physical-Y, unconverted), a `rowspan` cell's own row-axis
  sizing (it is not yet computed as the combined row-axis extent of every row it spans, the way
  `GetCellWidth` already sums a `colspan` cell's extent across the *column* axis — a `rowspan` cell
  currently sizes and can end up overflowing past the table's own row-axis bound; the row-tracking
  bookkeeping around it — `rowMaxBottom`, `CloseSpanningCell` — is axis-aware and no longer corrupts its
  *non-spanning* row siblings' positions, but the spanning cell's own extent is still wrong), `colspan`
  straddling a vertical table's row axis, real per-row pagination of a vertical table's own content (it is
  monolithic instead — see "What now works" above and the general per-vertical-content-fragmentation gap
  below), and `vertical-align`'s content-alignment-*within*-a-cell behavior (cell *size* is
  writing-mode-aware; `CssLayoutEngine.ApplyCellVerticalAlignment`'s own internal positioning of a cell's
  content is not yet).
- **Multi-column** ([#764](https://github.com/jhaygood86/PeachPDF/issues/764)) has no real column
  arrangement under a vertical writing mode — `column-count`/`column-width` are inert there, and a
  vertical-writing-mode multicol container falls back to ordinary single-column block flow (`CssLayoutEngineColumns.Layout`
  takes the same path a `column-count: 1`/auto-width container already does). Unlike Table's rows/columns,
  this isn't a narrower "arrangement now, fragmentation later" split: every column in this engine has to
  share one band along the block axis (`FragmentainerContext`'s own `(Top, Bottom)` tuple, hardcoded to
  physical Y at the type level — the same primitive the page-level pagination driver uses), while columns
  differ along the inline axis — physical Y for `vertical-rl`/`vertical-lr`, the very axis the shared band
  is pinned to. Real column arrangement would need that shared band to become axis-agnostic, which ripples
  into the page-level pagination invariants ~50 other files document — out of scope here. The fallback
  needs no `MonolithicContent` treatment of its own (unlike a vertical table's row loop): ordinary
  single-column block flow already supports real pagination.
- **A vertical-writing-mode Flexbox container's own top-level definite-main-size resolution has no
  aspect-ratio-on-width fallback** ([#772](https://github.com/jhaygood86/PeachPDF/issues/772)). The
  pre-existing `aspect-ratio`-driven auto-height fallback
  (`CssLayoutEngine.TryGetAspectRatioHeight`) stays scoped to the physical Y dimension; a `column`-direction
  container under a vertical writing mode (main axis physical X) has no equivalent width-driven fallback,
  since the width-side helper (`TryGetAspectRatioWidth`) is documented as unsafe for a stretch-fit block-
  level box, which a flex container's own auto width already is. A narrow, deliberate boundary matching an
  already-narrow pre-existing feature's own scope, not a general Flexbox regression.
- **A vertical box's own content never fragments across a page boundary**
  ([#767](https://github.com/jhaygood86/PeachPDF/issues/767)) — being monolithic, it is moved whole to the
  next page if it doesn't fit the current one, or displaced-per-band (never resized) if it fits nowhere; it
  cannot yet split its own content the way ordinary horizontal flow does.
- **True OpenType vertical metrics (`vhea`/`vmtx`/`VORG`) are parsed but not yet consulted**
  ([#770](https://github.com/jhaygood86/PeachPDF/issues/770)) by layout or paint — glyph advance/positioning
  under vertical writing modes still uses the same horizontal-advance metrics as `horizontal-tb`, just
  reinterpreted geometrically (rotated), not real vertical typesetting metrics.

The reader-facing note is in `docs/html-css-support.md`'s `writing-mode`/`text-orientation` rows.
