# Per-page horizontal reflow is scoped to block content, multicol, tables and flex — not grid

**Per-page horizontal reflow (issue #143) is scoped to ordinary in-flow block-level content.** The
width seam (`CssLayoutEngine.GetBoxWidth`/`LineContentRightOf` → `HtmlContainerInt.PageContentRightOf`,
gated by `IsUnconstrainedMainColumn`) reflows an auto-width, unconstrained (no `max-width`) block-level
box whose entire containing-block chain up to the page area is itself such a box — no longer limited to
root/`<html>`/`<body>` (see "No longer a gap" below). The per-line text-wrap measure additionally
requires the block itself to be an ordinary in-flow box that spans its own containing block's full width
(`CssLayoutEngine.FillsContainingBlockWidth`): a table cell (CSS2.1 §17.5's column-width algorithm) or a
flex/grid item (its own engine's sizing) shares its containing block's width with siblings rather than
claiming all of it, and a float or an absolutely/fixed-positioned box is sized against its own placement
— each keeps its one already-correct, page-independent measure unchanged rather than being fed its
containing block's edge as if it filled the whole of it. Each remaining case is a genuine CSS Paged Media
3 §5 ("the edges of the page area act as a containing block for the layout that occurs between page
breaks") deviation, tracked as its own issue: grid (the same shape flex had before #196 closed it - see "No longer
a gap" below; tables, multicol and flex all closed the same family of gap); and named-page (`page: <name>`) L/R and size overrides,
whose width→height→page-name feedback the bounded reflow loop does not *formally* drive to convergence
across a run that spans several physical pages under one active name
([#202](https://github.com/jhaygood86/PeachPDF/issues/202), narrowed — see below).

**No longer a gap**: an auto-width main-column block spanning a page boundary now genuinely re-wraps its
own text at each fragment's own measure (css-break-3 §5.1: "a fragment ... is sized ... using the size of
its own fragmentainer"), one line at a time (`CssLayoutEngine.LineContentRightOf`, refreshed at every
line start/wrap point) — it no longer shares one inline size across every fragment the way an ordinary
(non-text) box still does.

**Also no longer a gap** ([#876](https://github.com/jhaygood86/PeachPDF/issues/876)): the block's own
outer border box (`CssBox.Size`/`ActualRight`, and therefore its painted background/border, and any
non-text content sized against its own width) now resizes per fragment too, catching up to the text that
already reflowed — `FragmentEmitter.Draft.InlineExtentDeltaWidth`, computed by `ComputeInlineExtentDelta`
and consumed at `FragmentEmitter.ExtentOf`, mirrors `Draft.FixedSizeDeltaWidth`'s own
delta-from-the-single-global-value shape (Layer K), using the exact same eligibility test
(`CssLayoutEngine.IsUnconstrainedMainColumn`) and formula (`GetBoxWidth`'s own auto-width branch,
substituting this slot's own content-right edge) the box's single global width was originally resolved
with — so a box's own frame and its already-reflowing content always agree on whether they are eligible
at all. This is the shared fragment-tree contract every later layer that needs a differently-sized
fragment (tables/flex+grid/multicol) depends on existing first; tables', multicol's and flex's
own container-width gaps closed via #197/#198/#196, see below.

**Also no longer a gap**: `HtmlContainerInt.UseVariableInlineMeasure` (renamed from
`UseVariablePageWidth`) now fires on a per-page `size` override with no margin override at all, not just
a margin override — a document that only mixes physical page sizes (e.g. a landscape named page for a
wide table, with the same margins everywhere) now gets main-column reflow exactly as a margin-only
override already did; `PageContentRightOf` reads this slot's own `PageBandGeometry.SheetWidthPt` rather
than the document's base sheet width. Separately, the specific mechanism #202 named as blocking a named
page's own content from re-wrapping at all — a box that opens a named page is measured
(`CssBox.ResolveOwnInlineSize`) *before* its own placement registers that name
(`HtmlContainerInt.RegisterNamedPageElement`), so it silently measured against the previous name's
geometry — is fixed: `CssBox._measureResolvedAgainst` records the measure actually used at resolve time,
and `InlineSizeCameFromAnotherPagesMeasure` compares a fresh lookup against *that* (not a second fresh
lookup, which agreed with the first because both ran after the registration that invalidated the slot) to
catch and correct it within the same pass. `PageAssignmentSignature`'s own fixpoint comparison closes a
further blind spot: it now pairs each box's page index with that page's own active name
(`PageGeometryTable.PageBandGeometry.ActiveName`), so two passes that agree on numeric page index but
disagree on which named-page rule is active there (because a width change shifted a name-transition
boundary onto a different physical page) are correctly told apart rather than wrongly accepted as
converged. What #202 still names and none of this closes: a named-page run that spans *several physical
pages* under one continuously-active name has a genuine width→height→page-name fixpoint (the content
width affects box heights, which affects which page-number boundary falls where, which can affect which
name is active at that boundary) that the bounded reflow loop does not *formally* prove converges — only
empirically observed, on this repo's own fixtures, to settle on the loop's first iteration once all three
fixes above are in place. See
[named-page-run-convergence-loop-is-bounded-not-guaranteed.md](named-page-run-convergence-loop-is-bounded-not-guaranteed.md)
for the accepted, narrowed remainder.

**Also no longer a gap** (#199, #200, #201): three related restrictions on which boxes participate in
per-page reflow at all have closed together, via one generalized eligibility rule.
`CssLayoutEngine.IsUnconstrainedMainColumn` no longer requires every level of a containing-block chain to
be root/`<html>`/`<body>` by tag name (`IsOrdinaryUnconstrainedBlock` instead) — any plain, auto-width,
`max-width`-free block-level box now qualifies as a chain link, so content nested below the main column
(e.g. inside an ordinary wrapper `<div>`) reflows exactly as a main-column box already did (#200); a
`max-width`-bearing wrapper still correctly disqualifies the chain, since a capped box can't be assumed to
genuinely span the page area. A percentage (or explicit-length `position: fixed`) width now resolves its
basis through the new `CssLayoutEngine.PageAwareWidthBasis` helper — the same page-aware measure the
auto-width branch already used — instead of a single static `ContainingBlock.Size.Width`/`PageSize.Width`
value, so a percentage-width block (ordinary, absolutely-positioned, or fixed) tracks whichever page it
lands on rather than the page it happened to be resolved against on an earlier layout generation (#199).
And `HtmlContainerInt`'s initial-containing-block width seed (`Root.Size`, previously always the
document's base configured width) is now pinned to page 0's own resolved band
(`PageGeometryTable.PageBandGeometry.BandWidth`, added as the width counterpart of the pre-existing
`BandHeight`) via the new `IcbWidthSeed` helper, mirroring `CssLayoutEngine.GetBoxHeight`'s existing
height-side pin — so a percentage width (or an absolutely-positioned box's `left`/`right`-filled auto
width) that bottoms out at the true ICB resolves against the FIRST page's own (possibly
`:first`-overridden) area, not the document's base configured width, matching what already applied to
percentage heights. An explicit fixed-length `width` (unlike a percentage) still never varies by page —
correctly, since an author's fixed measurement shouldn't.

**Also no longer a gap** (#198): a multi-column container's own width now reflows across the pages it
spans. `CssLayoutEngineColumns.Layout` resolves `containerWidth` via `CssLayoutEngine.GetBoxWidth` exactly
as before, but now passes it the invocation's own `boxTop` (the fragmentainer this particular pass is
filling — already computed, just further down, for its own per-continuation `startSlot` lookup) instead of
leaving it default to `columnsBox.Location.Y`, the box's fixed start-page position that never changes
across the several fresh `Layout` invocations one continuing container makes. Since a resumed continuation
already re-enters this method fully (there is no early-return the way flex's resume path takes — see
#196), each page's own invocation now derives its own `columnCount`/`columnWidth`/`pitch` from that page's
own width, rather than every continuation reusing the start page's.

**Also no longer a gap** (#197): a table's own width now resolves against whichever page the table
itself starts on, rather than its containing block's single, page-0-cached `Size.Width`.
`CssLayoutEngineTable.GetAvailableTableWidth` resolves the horizontal (non-`_isVertical`) case through
`CssLayoutEngine.PageAwareWidthBasis`, keyed to `_tableBox.ClientTop` — the table's own starting position,
which (unlike multicol's independent per-page column runs) stays fixed across the table's whole lifetime
even once it starts continuing across further pages (`ResumedRowTop`'s own remarks explain why a
continuation's rows are placed relative to that same fixed position). This is deliberately **not** the
same shape as #198's fix: a table's columns are one shared grid spanning every row, so - unlike
multicol - varying the resolved width *per continuation* would misalign a row's own cells against a
sibling row's on a different page of the same table. The fix is narrower and structurally simpler than
#198's: resolve once, correctly, against wherever the table begins, and never re-resolve for the rest of
its lifetime — exactly how an ordinary (non-text) block's own width already worked before and after
#199-#201.

**Also no longer a gap** (#196): a block-level flex container that continues across pages of different
measures sizes each fragment to the page it is on (css-break-3 §5.1), and so do its items. Flex is unlike
multicol in that it has no single seam to correct: its resume path returns before any sizing, and an item's
size is *pinned* by the commit pass, so the fix is several pieces that have to agree.

- **The container's frame** is *stated* by the engine per fragmentainer (`CssLayoutEngineFlex.StateContainerFrame`
  / `StateSpannedFrames`, `HtmlContainerInt.RecordInlineFrame`, consumed in `FragmentEmitter.ExtentOf`) rather
  than derived by the emitter the way `ComputeInlineExtentDelta` derives an ordinary block's, because the
  frame is the product of the flex algorithm. `ComputeInlineExtentDelta` still declines a flex container.
  Statements are keyed by slot and absolute (X, width), and are looked up at materialization, not when a
  slot's draft is frozen: a later pass states the frame of an earlier slot.
- **A wrapping row's lines** are collected, sized and placed one at a time against the measure of the page
  each is expected to land on (`BuildLinesPerPage`), seeded by where relocation actually put them
  (`LandingMeasures`), bounded at `MaxPerPageAttempts` attempts.
- **A line that continues onto a page of another measure** is re-fitted on the resumed pass
  (`RefitStartedLine`): sizes re-derived from CSS alone (`RederiveItem`/`DistributeFlex`), the frame each item
  had in every earlier slot stated *before* its live geometry moves (`MoveFrame`), the blocks the unfinished
  content resumes into re-fitted top-down (`RefitContinuingDescendants`), and the line's height re-derived
  when it finishes (`GrowRefitLine`), moving the lines below and the container's bottom with it. Items no
  pass resumes (a fixed-height box, an image) get their per-page frames stated at the end of the fresh pass
  (`StateSpannedLineFrames`).
- **The stale pin** (`ItemContentCommit.UnpinIfPinned`): `CommitLayout` pins an item's `Width`/`Height`
  permanently and nothing put the authored values back, so a later measurement of the same item read its own
  earlier answer as authored. Only observable once something measures an item against a second measure.

Verified against Prince, which lays these documents out the same way, and against Chromium, which does *not*:
it re-sizes only the container and lets every item overflow it at its first page's width. A **column**
container's items keep the cross size they started with, in both of them, and so here.

Left as it is, each a genuine css-break-3 §5.1 deviation - a fragment sizes to its own fragmentainer - and
each tracked: a flex line that is *unstarted* on a page reached only because a re-fitted line above it grew
is sized for the page it was expected to land on, not the one it did; an item that does not stretch
(`align-self` other than `stretch`) keeps the offset its earlier line height gave it when a re-fitted line
grows; a flex container inside a multi-column container is not re-fitted per column; and the grid analogue
of all of this.
