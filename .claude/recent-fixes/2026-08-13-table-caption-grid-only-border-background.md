# A table's own border/background now wrap the row grid only, not its caption (issue #721)

CSS 2.1 §17.4 models a `<table>` with a caption as living inside an anonymous "table wrapper
box": the table's own `border`/`background`/`padding` apply to the row grid only, and the
caption sits outside that box. PeachPDF didn't synthesize a separate wrapper box - the
`<table>` element's own `CssBox` (`_tableBox` in `CssLayoutEngineTable`) was the box the caption
was laid out inside, and its `Location`/`ActualRight`/`ActualBottom` already spanned grid+caption
before this fix, so a table with a visible `border`/`background-color` painted that border/
background behind the caption's own area too. Recorded as an accepted gap alongside the
`caption-side` implementation (#705) and tracked as #721; this closes it.

**Load-bearing idea, found by working through several designs before settling on one**: closing
this gap does *not* need a real ancestor "table wrapper box" (the structural change #705's own
gap note assumed it would). `_tableBox` keeps every existing role unchanged - margin, position,
float, block-flow participation, page-break relocation, DOM/HtmlElement linkage, and (critically)
its own `Location`/`ActualRight`/`ActualBottom`, which still span the *combined* grid+caption
assembly exactly as before, since every other consumer of this box already expects that. Instead,
a captioned table gets one anonymous leaf child - `CssBox.TableGridDecorationBox` - sized to the
row grid's own border-box rect once layout knows where it starts and ends
(`CssLayoutEngineTable.FinalizeGridDecorationBoxGeometry`), carrying a *copy* of `_tableBox`'s own
border/background (`CssBox.AdoptBorderAndBackgroundFrom`, a whole-style-area reference copy - see
its own remarks for why that's superior to enumerating properties). `_tableBox`'s own border/
background paint is then suppressed (`CssBox.SuppressOwnBorderPaint`, alongside the pre-existing
`SuppressOwnBackgroundPaint`) so the two boxes don't double-paint. A captionless table (the
overwhelming majority) never creates this box - zero cost, zero behavior change.

**A structural ordering trap, found by tracing `RemoveHeaderFooterFromTree`/
`RestoreStructureFromAnyPreviousRun` rather than by running anything**: the decoration box has to
be created and positioned as `_tableBox.Boxes[0]` *before* anything else in the same pass captures
an index into that list. `RemoveHeaderFooterFromTree` captures `_headerIndex`/`_footerIndex` via
`_tableBox.Boxes.IndexOf(...)`, bakes it into a `CssProxyBox`'s own `SourceIndex`, and a *later*
pass's `RestoreStructureFromAnyPreviousRun` replays that index to reinsert the detached group.
Creating the decoration box afterward (e.g. once real geometry is known, at Step 7) would insert
it *after* an index an earlier pass already captured and baked into a proxy, silently shifting
every subsequent header/footer restore by one slot - reordering `<colgroup>`/`<thead>` relative to
each other in the internal tree (harmless for rendering, since neither is itself painted based on
that order, but exactly the kind of drift this codebase's own comments warn about elsewhere).
Fixed by splitting the work in two: `EnsureGridDecorationBoxStructure` (structural: create/
reposition-to-front, called right after `AssignBoxKinds`, before `InsertEmptyBoxes`/
`RemoveHeaderFooterFromTree` run) and `FinalizeGridDecorationBoxGeometry` (geometry only, called at
Step 7 once the grid's true bottom is known). Every pass repositions the decoration box to the
front unconditionally rather than trusting it stayed there - cheap on a small list, and immune to
whatever else churned `_tableBox.Boxes` in between.

**A second, purely arithmetic fix, verified by tracing the numbers rather than assumed**: the top
caption used to be laid out starting at `_tableBox.ClientTop` (inside the table's own border,
which is exactly the bug), while the row grid's own `startY` already added the border width back
in via that same `ClientTop`. Anchoring the caption at `_tableBox.Location.Y` instead (the
combined assembly's true top, with nothing above it) and re-deriving `_topCaptionsHeight` from
that same anchor leaves `startY`'s own formula *algebraically unchanged* - traced by hand with
concrete numbers before touching the code, since the two anchors differ by exactly
`ActualBorderTopWidth`, which `startY`'s formula already adds back via `ClientTop`. The row grid
never moves; only the caption moves up (closing the border-width gap that used to sit above it)
and the border's own paint moves down to the grid's own top. The bottom caption's own start
position needed an actual (not just re-anchored) change: it now starts at `gridBorderBoxBottom`
(the grid's own border-box bottom, captured before the bottom caption extends `contentBottom`)
rather than the bare `contentBottom` it used to start from, which put the caption *before* the
border that then had to be drawn beneath it.

**A pagination gap that would have been invisible without rasterizing a multi-page case
specifically**: `FragmentPainter`'s multi-page bottom-border-truncation (drawing a table's outer
bottom border at the page-break Y on an intermediate page, rather than off-page at the box's own
unbroken bottom) was gated on `box.DerivedStyle.ActualDisplay == Keywords.Table` - which the
decoration box, deliberately `Keywords.Block`, never matches. Relaxed to key purely on
`box.PageBreakBottoms != null` (only `CssLayoutEngineTable` ever sets that field, on either box -
`FinalizeGridDecorationBoxGeometry` mirrors `_tableBox.PageBreakBottoms` onto the decoration box by
reference every pass), which is both a strict simplification and the fix: without it, a captioned,
bordered, multi-page table would draw its bottom border on every intermediate page instead of only
at the table's true end.

**Deliberately not attempted**: a spec-literal anonymous wrapper *ancestor* (the structural change
#705's own gap note assumed this would need) - margin/position/float ownership never needed to
move, since `_tableBox` already correctly plays that role for every existing consumer. Also not
attempted: gating decoration-box creation on the table actually having a non-none border/
background (it's created for any captioned table, even one bordered only on its cells) - the
overhead is one childless box that paints nothing, and the added complexity of checking in advance
wasn't worth it.

**Three more defects found by a post-change review pass, not by the tests written alongside the
feature** (per this repo's own convention of running one before calling a non-trivial change done):

1. *A second double-count, in the flow-extent arithmetic rather than the paint rect.* Once the
   bottom caption's own start position was moved to `gridBorderBoxBottom` (already
   `ActualBorderBottomWidth` past the grid's content edge), the pre-existing, untouched final line
   `_tableBox.ActualBottom = contentBottom + _tableBox.ActualBorderBottomWidth` added that same
   width a *second* time on top of the caption's now-already-offset bottom - leaving
   `_tableBox`'s combined extent (and so a following sibling's position) one border-width taller
   than the caption's own visible bottom edge, for any bottom-captioned table with a border. Fixed
   by branching: `_tableBox.ActualBottom` becomes the caption's own returned bottom when one was
   laid out this pass, `gridBorderBoxBottom` otherwise - verified by hand to reproduce the exact
   pre-fix numeric value for a captionless-of-bottom-caption table (zero regression there) while
   removing the double count for the bordered+bottom-captioned case.
2. *`box-shadow` painted twice.* `AdoptBorderAndBackgroundFrom` deliberately copies the *whole*
   Border style area onto the decoration box (not a property-by-property enumeration) specifically
   so it also carries `box-shadow` - but the border-stroke suppression (`SuppressOwnBorderPaint`)
   only gated the stroke draw call, not `FragmentPainter`'s separate outset/inset `PaintBoxShadows`
   calls, which read `box.BoxShadow` unconditionally. A captioned table with both its own border and
   a `box-shadow` painted the shadow from both boxes - once at `_tableBox`'s own, still
   caption-inclusive rect (the very defect this fix targets), once at the decoration box's correct,
   grid-only one. Fixed by gating both `PaintBoxShadows` calls on `!box.SuppressOwnBorderPaint` too.
3. *The decoration box broke "first in-flow child"/"no previous sibling" invariants several other
   subsystems depend on*, by permanently occupying a captioned table's own `Boxes[0]` - the exact
   slot `CssBox.IsOutsideMarker`'s own doc comment already flags as sensitive (an outside `::marker`
   sits there for a list item, for the identical reason). `BreakPropagation.IsInFlow` (so a forced
   `break-before`/`break-after` on the caption itself now propagates to the table again),
   `DomUtils.GetPreviousSibling`, `CssCounterEngine`'s own sibling walk (so a caption using
   `content: counter(...)` sees whatever preceded the table, not this synthetic box), and
   `CssBox.FoldOwnAdjoiningTopMargins`'s margin-collapse lookahead chain (so a borderless table's
   real first-in-flow child - its caption - is what an ancestor's collapsed top margin actually
   reaches, not a synthetic 0) all needed the same one-line exclusion, mirroring the existing
   `IsOutsideMarker` precedent exactly rather than inventing a new pattern - added a matching
   `CssBox.IsTableGridDecorationBox` flag alongside the existing `TableGridDecorationBox` pointer,
   set once at construction, checked at each of those four sites.

None of the three had a failing test until this pass - (1) is arithmetic no purely-geometric
assertion happened to exercise on a bordered table, (2) has no `box-shadow` coverage anywhere in
this change's own new tests, and (3) requires a CSS combination (`break-before`/margin-collapse/
counters specifically on a `<caption>`) narrow enough that neither the original implementation nor
its own tests reached it. Regression tests were added for (1) and (2) directly (an `ActualBottom`
equality assertion on the existing bottom-caption-with-border test, and a `box-shadow` paint-count
test); (3) has no regression test - narrow enough, and mechanically identical enough across all four
sites, that a test for one would not have caught the others, and the fix is the same one-line
pattern the codebase already established for `::marker`.

**Evidence**: 9 new unit tests total (`CssLayoutEngineTableTests.cs`: grid-decoration geometry for top
and bottom captions, a captionless-of-border-or-background table painting unchanged, border paint
confined to the decoration box's own rect - a `RecordingGraphics`-based check added in the review
pass alongside the geometry-only ones, since a geometry assertion alone isn't proof for anything
touching the paint layer per this repo's own testing convention - background paint likewise, a
`box-shadow` paint-count regression test, and an `ActualBottom` regression assertion on the existing
bottom-caption-with-border test; `CssLayoutEngineTablePageBreakTests.cs`: `PageBreakBottoms` mirrored
onto the decoration box for a multi-page captioned table) plus the 4 existing `caption-side` tests
from #705, unchanged and still passing (they assert against `_tableBox`'s own geometry, which this
fix deliberately leaves untouched). Full suite (8756 tests, net8.0) passes; `dotnet build -t:Rebuild`
is warning-free; 100% diff coverage against `main`. Visual verification via both PDFium and MuPDF
rasterization, agreeing, for: a top-caption bordered/filled table, a bottom-caption one, and a
59-row bordered/filled captioned table spanning two pages (confirming the multi-page
border-truncation fix specifically - the side/bottom borders truncate and close correctly across
the page break).
