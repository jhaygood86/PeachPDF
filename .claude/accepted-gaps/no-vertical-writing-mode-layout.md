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

**Block-level and orthogonal-flow children of a vertical box**
([#760](https://github.com/jhaygood86/PeachPDF/issues/760)) now work too:
`CssBox.LayoutContents` dispatches a `vertical-rl`/`vertical-lr` box with block-level children (not
inline-only, so not `CreateVerticalLineBoxes`; not a multi-column container, so not
`CssLayoutEngineColumns`) to a new `CssBox.LayoutVerticalBlockChildren`, the box-level counterpart of
`CreateVerticalLineBoxes`'s own word-level column stacking. Each in-flow child runs its own, completely
untouched, recursive `LayoutContents` dispatch — driven entirely by *its own* `WritingMode.Value`, which
was already correctly resolved regardless of its parent's own stacking axis — and is then stacked, as one
atomic already-laid-out unit, along the parent's own block axis (physical X: right-to-left for
`vertical-rl`, left-to-right for `vertical-lr`, via `WritingModeFrame.BlockStartIsRight`), all at the same
cross-axis (physical Y) start. This is what lets an **orthogonal-flow child** — one whose own resolved
`writing-mode` differs from its parent's, per
[CSS Writing Modes 4 §4.3](https://www.w3.org/TR/css-writing-modes-4/#orthogonal-flows) — "just work" with
no special case at all: a `horizontal-tb` block nested inside a `vertical-rl` parent lays its own lines out
along physical Y exactly as it would anywhere else, while the new stacking loop only ever reads back its
resulting physical width and treats it as one atomic unit, the same way an atomic replaced element
(`<img>`) already is. Auto width/height on the vertical box itself shrink to the accumulated block-axis/
cross-axis extent of its children, reusing `CssLayoutEngine.ShrinkAutoWidthTo` (widened to `internal`) —
the same block-start-stays-fixed mechanism the inline-only case already established.

Such a box is treated as monolithic with respect to its parent's own page fragmentation, the same
`MonolithicContent.IsUnresumableOrthogonalFlow` treatment the inline-only case and a vertical table already
get (that predicate no longer requires `DomUtils.ContainsInlinesOnly` — it now covers every box
`CssBox.LayoutContents` routes to *either* `CreateVerticalLineBoxes` or `LayoutVerticalBlockChildren`,
excluding only a box that runs an engine of its own or establishes its own multi-column context). This is a
real, deliberate behavior change from before #760 landed, not an incidental side effect: previously, a
vertical box with block children silently fell back to ordinary `horizontal-tb` physical-Y block stacking,
which happened to paginate normally (each child a separate, resumable fragment) purely because the bug
existed. Once real block-axis stacking landed, that box's own content became genuinely monolithic — moved
whole to the next page (or displaced-per-band) rather than sliced, exactly like the inline-only case. Real
per-child fragmentation of a vertical box's block content remains tracked separately
([#767](https://github.com/jhaygood86/PeachPDF/issues/767)).

Deliberately scoped down, mirroring `CreateVerticalLineBoxes`'s own scope: every child sits at the same
cross-axis start (no cross-axis wrapping); margins between stacked siblings are summed rather than really
collapsed ([#776](https://github.com/jhaygood86/PeachPDF/issues/776)); a floated/absolutely/fixed-positioned
child is routed through the ordinary, physical-Y-oriented `LayoutBlockChild` path unchanged rather than
given block-axis-aware float/positioning logic of its own (extending the existing #768 scope boundary);
and an orthogonal auto-width child stretch-fills to the parent's available physical width rather than
performing real CSS Writing Modes 4 §4.3 shrink-to-fit sizing
([#777](https://github.com/jhaygood86/PeachPDF/issues/777)).
Verified with `VerticalWritingModeLayoutIntegrationTests.cs` (block-axis stacking direction for both
`vertical-rl`/`vertical-lr`, auto width/height shrink, the cheap-margin-sum boundary, an orthogonal
`horizontal-tb` child's own line flow plus its atomic placement, nested vertical-in-vertical composition,
the `IsUnresumableOrthogonalFlow` predicate for the has-block-children case, and the monolithic-overflow
pagination-scope change above) and the existing full test suite passing unchanged.

**A `direction: rtl` vertical box's block children now anchor to the correct physical edge too**
([#778](https://github.com/jhaygood86/PeachPDF/issues/778), a scope boundary found by a post-change review
of #760). `LayoutVerticalBlockChildren`'s child loop still always places each child flush against
`ClientTop`, growing down — the only placement possible before a child's own cross-axis extent (height) is
knowable, since unlike its block-axis extent (width, resolved up front by `ResolveOwnInlineSize`) a normal
or orthogonal child's height generally isn't known until its own content lays out. Where `direction: rtl`
actually wants the physical bottom (CSS Writing Modes 4's own inline-start edge for a vertical box under
`rtl`, already correctly modeled by `WritingModeFrame`'s own `_inlineStartIsBottom` for word placement in
`CreateVerticalLineBoxes`, now also exposed as `WritingModeFrame.InlineStartIsBottom`), every stacked child
is collected into `CssBox._pendingCrossAxisRtlReflection` and reflected within `[ClientTop, ClientBottom]`
from `PerformLayoutEpilogue`, *after* `CssLayoutEngine.ApplyHeight` has settled this box's own real final
height — the same "lay out everything forward, then reflect once the far edge is known" shape
`CssLayoutEngineTable.ReflectRowAxisForVerticalRl` already uses for a `vertical-rl` table's own row axis,
reusing its exact reflection formula (`delta = (min + max - farEdge) - nearEdge`, then `OffsetTop` to
deep-translate the child's own already-laid-out words/rectangles/descendants along with it) one axis over.
Reflecting from the epilogue rather than inline inside `LayoutVerticalBlockChildren` itself is load-bearing,
not a style choice: a first cut computed the far edge locally (shrink-wrapped content extent for auto
height, or the raw `height:` value for a definite one) and a post-change review caught that this anchors
children against a pre-`min-height`/`max-height`-clamp edge — a `min-height` taller than the content-driven
extent left every child hanging from the wrong edge, reintroducing a version of #778's own symptom. Using
the box's own live `ClientTop`/`ClientBottom` from the epilogue, once `ApplyHeight` has already resolved
`min-height`/`max-height`/percentage-against-an-indefinite-containing-block, sidesteps re-deriving any of
that resolution a second time. Verified with new `VerticalWritingModeLayoutIntegrationTests.cs` cases
(auto-height reflection with a shorter and a taller sibling, an explicit-height box taller than every
child, a `min-height` taller than every child's own content — the case the review caught — and
`vertical-lr` + `direction: rtl` to confirm the cross-axis reflection is independent of which edge the
block axis itself grows from) and the existing full test suite passing unchanged.

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
closest available per-character constant that can never under-advance, for a font with no real vertical
metrics of its own (see the real-`vmtx`/`VORG` paragraph below for the font that does have them). Verified
with `VerticalOrientationTableTests` (table lookups against real UCD data), `TextOrientationIntegrationTests`
(word-splitting via a `CssBox`+layout harness, and `RGraphics`-call-sequence assertions for both the
per-character upright paint path and the rotated path via a `RecordingGraphics` mock — 5 of 10 tests
confirmed meaningful by failing when the feature was temporarily disabled), and both PDFium and MuPDF
rasterization of the `writing_mode` showcase's section 8 (mixed CJK upright next to rotated Latin/digits,
upright-forced, and sideways-forced examples, all agreeing between renderers with no overlapping or
garbled glyphs).

**Real OpenType vertical advance metrics** ([#770](https://github.com/jhaygood86/PeachPDF/issues/770)) are
now consulted for an upright run's down-the-column advance, in both the HTML and SVG pipelines, when the
resolved font actually carries real `vhea`/`vmtx` data (`OpenTypeDescriptor.HasVerticalMetrics`/
`GlyphIndexToVerticalAdvance`, exposed to layout/paint via `RFont.HasVerticalMetrics`/`GetVerticalAdvance`,
only overridden by `FontAdapter`). Gated deliberately: the large majority of fonts carry no vertical metrics
at all, so `CssLayoutEngine.NaturalWordSize`'s and `FragmentPainter.Text.cs`'s `PaintUprightVerticalRun`'s
upright branches (and their SVG counterparts, `SvgRenderer.LayoutGlyphs`/`PaintUprightGlyph`) fall back to
the pre-existing line-height approximation above byte-for-byte whenever `HasVerticalMetrics` is false — this
closes the gap only for fonts that actually have real vertical typesetting data (mainly professional CJK
vertical fonts, including this repo's own bundled `NotoSansJPSubset.ttf`/`BundledFonts.Cjk`, confirmed to
genuinely carry real `vhea`/`vmtx`) without changing output for every other font. The rotated/`sideways`
paths (`NaturalWordSize`'s rotated branch, `FragmentPainter.SidewaysRotation`, SVG's `PaintRotatedGlyph`)
are deliberately untouched — CSS Writing Modes' own spec intent and real browser behavior both use rotated
horizontal metrics for `sideways` orientation, not vertical ones, so that path was never the approximation
the gap was about.

A real `vmtx` advance is legitimately, routinely narrower than the font's own line height (a CJK vertical
font typically advances one em per character; ascent+descent is usually well over one em) — this repo's own
painting-test convention (rasterize and look, not just assert a token/count) caught that once real metrics
made the per-character step narrower than the font's line height, `RGraphics.DrawString`'s "always paints a
full line-height-tall span from its anchor" behavior reintroduced exactly the bleed-into-the-next-character
overlap the original line-height approximation (above) existed to avoid — an actual, visually confirmed
regression against the real bundled CJK font (not a hypothetical), found only by rendering a real repro
through `PdfGenerator` and rasterizing it, since every self-consistent layout/paint-agreement test still
passed throughout (both sides used the same, too-small, advance). `PaintUprightVerticalRun`/
`PaintUprightGlyph` now `PushClip`/`PopClip` each upright character to its own reserved cell before drawing
it whenever `HasVerticalMetrics` is true, confining a taller-than-its-cell glyph's paint rather than letting
it bleed into whatever's next; the line-height fallback needs no clip, since its advance already equals the
full painted span by construction. Verified by rendering the real repro through both PDFium and MuPDF before
and after the clip fix. One SVG-specific trap surfaced along the way: the natural "unbounded" cross-axis
clip (`double.MinValue`/`MaxValue`) silently broke under `RenderInto`'s own viewBox-to-viewport transform
(the extreme coordinates overflow through that matrix multiply), making every upright glyph invisible — a
finite, merely-generous margin around the glyph's own position fixed it without reintroducing any real
cross-axis clipping risk.

**`VORG`-derived vertical-origin *positioning*** ([#775](https://github.com/jhaygood86/PeachPDF/issues/775),
as opposed to `vmtx` advance above) is now consulted too, gated separately from advance: `RFont
.HasVerticalOrigin`/`OpenTypeDescriptor.HasVerticalOrigin` require a real `VORG` table specifically (`vorg
!= null`), not merely `vhea`/`vmtx` (`HasVerticalMetrics`) — a font with vertical advance data but no real
`VORG` only offers `vhea.ascent` as a positioning fallback, a value not designed to mean "vertical origin"
the way a real `VORG` entry is, so extending the shift to that weaker signal was deliberately left out. A
first attempt (during #770) shifted each character's paint anchor by `GetVerticalOriginY(rune) - Ascent`;
combined with the clip-per-cell fix above it cropped away most of each glyph and was reverted, on the
mistaken conclusion that "VORG-aware positioning needs a per-glyph-aware paint primitive this repo does not
have yet." That conclusion was wrong: rigorous re-derivation against the actual OpenType spec text (`vorg`/
`vmtx` pages, `learn.microsoft.com/en-us/typography/opentype/spec/`) found the reverted attempt's sign was
simply inverted (`y -= (originY - Ascent)` instead of the correct `y += (originY - Ascent)`) — tracing
`XGraphicsPdfRenderer.DrawString`'s own internal `cyAscent` shift confirmed it uses the exact same
`Ascender` field `RFont.Ascent` is built from, which is the precondition the corrected derivation needs. No
new paint primitive was required; the existing per-character `DrawString` anchor just needed the right
formula.

A second, genuine spec gap surfaced during this work and was fixed in the same change:
`GlyphIndexToVerticalOrigin` didn't check outline format before trusting a `VORG` table, but the OpenType
spec states `VORG` "may only be used in CFF or CFF2 OpenType fonts" and "if present in OpenType fonts
containing TrueType outline data, it must be ignored." `HasVerticalOrigin` now requires `FontFace.glyf ==
null` (the same signal `IsColorFont` already uses to detect outline format) in addition to a real `VORG`
table, so a TrueType-flavored font's `VORG` (rare in practice) is never consulted, falling through to the
existing `vhea`/`os2`/one-em chain exactly as a font with no `VORG` at all does.

Verified with new `VerticalMetricsTablesTests`/`TextOrientationIntegrationTests`/`SvgTextWritingModeIntegrationTests`
coverage (provenance-checked against the real parsed `vmtx`/`VORG` table entries, not fragile raw-value
comparisons, since a CJK ideograph's real vertical advance height commonly coincides numerically with the
no-vmtx one-em fallback; the clip fix and the CFF-only restriction each have their own dedicated tests,
including a CFF+TrueType pair proving the restriction actually suppresses `VORG` rather than merely being
untested) and the full existing suite passing unchanged. Since none of this repo's bundled fonts carry a
real `VORG` table, the `VORG`-positioning path itself is only exercisable via a synthetic table appended to
a real font (the existing `VerticalMetricsTablesTests.cs` precedent, extended into
`SyntheticFontTables.cs` for reuse from integration tests too) — visually verified (both PDFium and MuPDF)
against a "well-behaved" synthetic origin matching the font's own `Ascender` (flush with its cell, no visible
shift, per the spec's own worked example) and a deliberately different one (a real, provably non-zero, but
still clean and non-overlapping shift), plus confirmation that every currently-shipped showcase/bundled-font
render is pixel-identical to before this change.

A post-change review pass for #775 found three more real bugs, all fixed before this landed: SVG's
`LayoutGlyphs` computed `GlyphInfo.OriginYOffset` *inside* the `HasVerticalMetrics` branch instead of
independently, so a CFF font with a real `VORG` table but no `vhea`/`vmtx` (`HasVerticalMetrics` false,
`HasVerticalOrigin` true) silently got no origin shift at all — flagged independently by three different
review angles, confirmed by a regression test built on exactly that font shape
(`Upright_AppliesVorgOrigin_AndClipsGlyph_OnFontWithVorgButNoVerticalMetrics`); the same clip-per-cell gate
from the `vmtx` work above (both `PaintUprightVerticalRun` and SVG's `PaintUprightGlyph`) only fired on
`HasVerticalMetrics`, so a `VORG`-shifted glyph on that same font shape painted unclipped — extended to
`HasVerticalMetrics || HasVerticalOrigin`, with a matching HTML-side regression test
(`Upright_AppliesVorgOrigin_AndClipsCharacter_OnFontWithVorgButNoVerticalMetrics`); and SVG's
`ApplyBidiReordering` recomputes `GlyphInfo.IsUpright` after rewriting a glyph to its bidi-mirrored
codepoint (pre-existing, from the #770 review pass) but didn't recompute `OriginYOffset` alongside it, so a
glyph that becomes upright-classified only *after* mirroring would keep a stale (zero, never-computed)
offset instead of the mirrored codepoint's real `VORG`-derived one — fixed to recompute both together. The
first two have dedicated regression tests; the third's precondition (RTL mirroring + an upright-orientation
classification change + a real `VORG` table on the resolved font, all at once) is narrow enough that a
faithful test would need per-glyph `VORG`-override support the test helpers don't have yet — verified by
code inspection and the full existing bidi suite (`SvgTextBidiTests`) passing unchanged, not by a dedicated
new test.

**SVG `<text>`/`<tspan>` `writing-mode`/`text-orientation`** now work too, via SVG's own independent
text-layout pipeline (`SvgRenderer.LayoutGlyphs`/`PaintGlyphs`) — no `WritingModeFrame` needed here, since a
`<tspan>` never establishes its own formatting context the way an HTML box does. `writing-mode` is
resolved once from the `<text>` root (threaded through `SvgTreeBuilder`'s existing `InheritedPaint`
carrier, the same mechanism `direction` already uses) and decides which axis the pen advances along — X
for `horizontal-tb`, Y for `vertical-rl`/`vertical-lr` (`sideways-rl`/`sideways-lr` and SVG 1.1's legacy
`tb`/`tb-rl` keywords fall back to `horizontal-tb`, matching the HTML pipeline's own scope).
`text-orientation` is genuinely per-glyph, unlike `writing-mode` — a `<tspan>` can override it — and
`mixed` classifies each glyph by the same shared `VerticalOrientationTable.IsEffectivelyUpright` the HTML
pipeline uses (extracted out of `CssBox` into the shared table itself so the two pipelines can never
disagree on the classification). SVG already operates one glyph at a time (no HTML-style word-splitting
needed), so `GlyphInfo.IsUpright` is just an added field; painting reuses the exact rotation matrix
construction explicit `rotate=""` has always used (`PaintRotatedGlyph`, extracted from that pre-existing
code), just defaulted to 90° instead of an author-specified angle — an explicit `rotate=""` still wins
over the orientation-driven default when both apply. `ApplyBidiReordering`'s own reflection-about-content-
span math was made axis-aware too (reorders along `Py` instead of `Px` under a vertical writing mode).
Verified with `SvgTextWritingModeIntegrationTests` (paint-call-sequence assertions against a real adapter,
mirroring `SvgTextBidiTests`'s established pattern — 6 of 8 tests confirmed meaningful by failing when the
feature was temporarily disabled) and both PDFium and MuPDF rasterization of the new `svg_vertical_text`
showcase.

Finding this working took one non-obvious fix: `RFont.Height`/`.Ascent` are lazily primed by that font's
own *first-ever* `MeasureString` call (`GraphicsAdapter.MeasureString` only calls `FontAdapter.SetMetrics`
the first time it runs for a given font instance — before that, both properties read back a `-1`
sentinel). HTML's `CssLayoutEngine.NaturalWordSize` never hits this because `CssBox.MeasureWordsSize`
always measures a word's natural horizontal size earlier in layout, priming the font before
`NaturalWordSize`'s own upright branch ever reads `Height`; `LayoutGlyphs` has no earlier pass, and its
upright branch read `Font.Height` directly without measuring anything first whenever a run started with
upright-classified glyphs — so a freshly-resolved (never-yet-measured) font's leading upright run advanced
by `-1` per character, landing the following rotated run back near the text's own start rather than below
the upright run, visually overlapping it. Fixed by always calling `g.MeasureString` before reading
`Font.Height`, regardless of which branch actually needs the measured value.

An 8-angle post-change review pass turned up five more real issues, all fixed before this landed: the same
font-priming gap recurred in `MeasureTextBounds`'s `<textPath>` bounds loop (`pathFont.Ascent` read with
nothing on that code path ever measuring `pathFont` first); an explicit `rotate="0"` was indistinguishable
from no `rotate` attribute at all, so it couldn't override automatic vertical rotation the way any other
explicit angle could; `GlyphInfo.IsUpright` was classified once from a glyph's pre-bidi-mirroring codepoint
and never refreshed after `ApplyBidiReordering` rewrites `Glyph` to its mirror codepoint for an RTL run;
`writing-mode`/`text-orientation` attribute parsing in `SvgTreeBuilder` was a hand-rolled string switch
instead of reusing `Map.WritingModes`/`Map.TextOrientations` (the same keyword tables the HTML CSS-OM
pipeline's own converters use — `SvgElement.WritingMode`/`TextOrientation` now store the real enum values,
not strings); and every glyph was shaped twice (once in `LayoutGlyphs` to prime metrics and compute
`Advance`, again independently in `PaintUprightGlyph`/`PaintRotatedGlyph` at paint time) — `GlyphInfo` now
caches its measured `Size` once in layout for paint to read back. The review also flagged a real
architectural question, fixed as an immediate follow-up rather than left as a tracked issue:
`FontAdapter.Height`/`.Ascent`/`.UnderlineOffset` are now resolved eagerly in `FontAdapter`'s own
constructor instead of lazily on that font's first `MeasureString` call — the underlying `XFont`'s
descriptor/metrics are already fully resolved by the time its own constructor returns
(`XFont.Initialize`/`CreateDescriptorAndInitializeFontMetrics` run synchronously), so the old lazy
`SetMetrics`-on-first-measure design was never a real data dependency, only an accident of where the
arithmetic happened to live. This closes the underlying bug class at its one true source: `GlyphInfo.Size`
in SVG's `LayoutGlyphs` remains (real, necessary work — every consumer downstream needs the measured
width/size regardless of orientation), but the SVG `MeasureTextBounds` fix above was pure priming with no
other purpose and is now removed as dead code, and neither pipeline has to remember "measure before
reading Height" as an unenforced convention going forward.

## What's still out of scope

- **Real margin collapsing between block-axis-stacked children of a vertical box**
  ([#776](https://github.com/jhaygood86/PeachPDF/issues/776)). `LayoutVerticalBlockChildren` sums the
  adjoining physical margins between two stacked siblings instead of performing real CSS 2.1 §8.3.1
  adjoining-margin collapse (max, not sum) — see `VerticalRl_MarginsBetweenBlockChildren_AreSummedNotCollapsed`.
- **An orthogonal auto-width child of a vertical box stretch-fills instead of shrink-to-fit**
  ([#777](https://github.com/jhaygood86/PeachPDF/issues/777)). CSS Writing Modes 4 §4.3 requires an
  auto-sized orthogonal child (its own writing-mode perpendicular to its containing block's) to be sized
  via shrink-to-fit against a constraint derived from the parent's own definite dimension;
  `LayoutVerticalBlockChildren` instead gives it ordinary block auto-width behavior (stretch to the
  parent's available physical width). This codebase has no general min-content/max-content/shrink-to-fit
  algorithm anywhere yet — only `CssBox.GetMinimumWidth()`, an unrelated longest-unbreakable-word
  measurement — so implementing real shrink-to-fit is a separate, larger undertaking.
- **Floats, absolute positioning, hyphenation, bidi reordering, `text-align`, and
  `box-decoration-break: clone`** are not honored inside a vertical box's own content
  ([#768](https://github.com/jhaygood86/PeachPDF/issues/768)). This now also covers a block-level
  out-of-flow (floated/absolutely/fixed-positioned) child of a vertical box's own block children: it is
  routed through the ordinary, physical-Y-oriented `LayoutBlockChild` path rather than given block-axis-aware
  float/positioning logic of its own — see `VerticalRl_RunningPositionedAndOutOfFlowChildren_AreSkippedFromStackingButDoNotCrash`.
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
- **SVG `<textPath>` always flows horizontally along its path**, ignoring the `<text>` root's own
  `writing-mode` — there is no vertical variant of path-following text (matches real browser behavior; not
  tracked as a gap to close, since the combination has no well-defined vertical layout to begin with).

The reader-facing note is in `docs/html-css-support.md`'s `writing-mode`/`text-orientation` rows and
`docs/supported-svg-features.md`'s Text section.
