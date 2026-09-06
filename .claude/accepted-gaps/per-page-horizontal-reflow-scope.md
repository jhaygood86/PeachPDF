# Per-page horizontal reflow is scoped to ordinary block content, not table/flex containers

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
breaks") deviation, tracked as its own issue: flex ([#196](https://github.com/jhaygood86/PeachPDF/issues/196)),
tables ([#197](https://github.com/jhaygood86/PeachPDF/issues/197)) — the horizontal analogue of the #166
engine-independence family — each container resolving its own width once against a single stored
measure regardless of which page it starts on (multicol closed the same gap - see "No longer a gap"
below); and named-page (`page: <name>`) L/R and size overrides,
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
fragment (tables/flex+grid/multicol) depends on existing first; tables/flex remain open under their own
tracked issues (#196/#197) — multicol's own container-width gap closed via #198, see below.

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
catch and correct it within the same pass. What #202 still names and this does not close: a named-page
run that spans *several physical pages* under one continuously-active name has a genuine
width→height→page-name fixpoint (the content width affects box heights, which affects which page-number
boundary falls where, which can affect which name is active at that boundary) that the bounded reflow
loop does not *formally* prove converges — only empirically observed, on this repo's own fixtures, to
settle on the loop's first iteration once the two fixes above are in place.

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
own width, rather than every continuation reusing the start page's. Tables (#197) and flex (#196) remain
open — a table's `GetAvailableTableWidth` bypasses `GetBoxWidth` entirely, and flex's resume path skips
its own width-computing code path on every continuation, so neither has a comparable single seam to
correct.
