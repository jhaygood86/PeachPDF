# Close the three gaps #1101 left open: line sizing, anchor-aware growth, percentage height (#1166, #1167, #1169)

`ResolveAtomicInlineDeclaredHeight`/`HeightenAtomicInlineRectangles` (added by #1101) made a declared
absolute-length `height`/`min-height` size an inline-flowed `inline-block`'s own painted box, but
deliberately left three narrower gaps open, each with its own accepted-gap file and tracking issue.
This change closes all three, plus a related codebase-wide refactor the third one's real fix required.

## #1166 — the line/flow doesn't reserve the declared height

`FinalizeFlowBoxExit` already had a `MaxBottom`-extension branch for exactly this, but it compared
against `box.ActualHeight` — which is never grown on this path, since `Size.Height` is never assigned
for an inline-flowed inline-block (that's the whole reason #1101's fix reads `CssLineBox.Rectangles`
instead). The branch was dead in precisely the case it was meant to cover. Fixed by comparing against
`ResolveAtomicInlineDeclaredHeight(box)` instead — a pure computation, not `Size.Height`.

This runs synchronously inside `FlowBox`'s own walk, entirely separate from `FinalizeLineBoxes`/
`ApplyVerticalAlignment`'s rectangle machinery (confirmed by reading the actual call graph: a block's
whole `FlowBox` pass completes, mutating `coordinates.MaxBottom` throughout, before `FinalizeLineBoxes`
ever runs — and `FinalizeLineBoxes` is never even passed `coordinates`, so nothing there could write
back to it regardless). This is why it's safe from the regression an earlier attempt at #1166 hit
during #1101's own development: growing the box's *rectangle* before alignment fed the grown height
into `ApplyVerticalAlignment`'s CSS 2.1 §10.8.1 baseline-extent fold and grew the line correctly, but
left the containing block's own height (already computed from `FlowBox`'s pre-alignment `MaxBottom`)
too short, overlapping the next sibling. This fix never touches that fold at all.

## #1169 — growth always extended downward, ignoring vertical-align's real anchor

`HeightenAtomicInlineRectangles` always grew a box's rectangle downward from its post-alignment top,
correct only for `vertical-align: top` and default-baseline-with-content. Added `AnchorOf` (classifies
which edge `ApplyVerticalAlignment`'s own per-case arithmetic already anchored at a height-independent
position — verified algebraically per case, not assumed) and `GrowAtomicInlineRectangle` (grows from
that edge: up for `bottom`/`text-bottom`, split evenly for `middle`, down for `top`/`sub`/`super`/
default-baseline-with-content).

One case stays exactly as before: the default `baseline` on an empty/`overflow`-non-`visible` box.
Its "baseline" is `AtomicInlineBaselineOf`'s fallback (`rect.Bottom + ActualMarginBottom`), read from
the box's still-natural, pre-growth rectangle *inside* `ApplyVerticalAlignment`, before growth ever
runs — an anchor-aware rewrite of exactly this case was tried and reverted during #1101's own
development (measured regression: an empty bordered box landed at `Y = -20` instead of `20`), and nothing
about this change makes that circularity any less real. Left as a narrower accepted gap.

## #1167 — percentage height/min-height was ignored, and why the real fix is bigger

The blocker on record was that resolving a percentage height needs to know whether the containing
block's height is definite (`CssBox.IsHeightCalculated`), which was only ever set — bottom-up — in
that box's own `ApplyHeight` epilogue, strictly after the inline-flowed box's own placement.

Reading `HasDefiniteHeight`/`ApplyHeight`'s actual assignment showed this doesn't have to be a
bottom-up question at all: every disjunct in `isRootWithPageHeight || isDefiniteHeight ||
isRatioHeight` bottoms out in either a pure CSS value, an already-top-down-resolved width (for the
aspect-ratio branch — width is resolved before a box's own content, confirmed against
`docs/architecture.md`'s own stated invariant), or the same question asked one level up the
containing-block chain. CSS Sizing 3 §4 defines "definite" exactly this way — recursively, over the
declared box tree — so the bottom-up cache was an implementation choice, not a spec necessity.

**What was actually done, beyond the minimum needed for #1167 alone** (the user asked for the old
field to be fully replaced, not just fed from new logic, and then asked for the paired `Size.Height`
value reads to be closed too, not left relying on the existing two-pass `ApplyHeight`/`ApplyParentHeight`
pattern):

- `CssBox.IsHeightCalculated` deleted outright. `CssLayoutEngine.IsHeightDefinite(CssBox)` replaces it
  everywhere (11 read sites across `CssBox.cs`/`CssLayoutEngine.cs`/`CssLayoutEngineTable.cs`) — a
  pure, recursive, on-demand function with the same three-way union, callable at any point during
  layout.
- `CssLayoutEngine.ResolveDefiniteHeightValue(CssBox)` — the value-returning twin — replaces every
  paired `ContainingBlock.Size.Height` basis read (5 sites) with `ResolveDefiniteHeightValue(...) ??
  Size.Height`. The `??` fallback is not defensive filler: by construction it's only ever exercised for
  a table cell (CSS 2.1 §17.5.3, row-stretch can grow it past its own declared height) or a box whose
  height comes from something other than its own `height` declaration (aspect-ratio; a flex/grid
  algorithm's result is deliberately excluded from this carve-out, see below) while establishing an
  independent formatting context a contained float could still grow (§10.6.7) — every other box's
  percentage resolution is now fully order-independent. This also mirrors `ApplyHeight`'s own
  min-height-floor-then-max-height-clamp-with-min-reflooring order, which the first draft of this
  function initially missed (caught by `HeightDefinitenessTests`, before it shipped).
- `ResolveAtomicInlineDeclaredHeight` now resolves the percentage case via both functions, gated on
  `IsHeightDefinite(box.ContainingBlock)`. Since this is the same function #1166's `MaxBottom` branch
  and #1169's `HeightenAtomicInlineRectangles` already call, the percentage case picks up line
  reservation and anchor-aware growth automatically — no second, corrective pass was built.

**Flex/grid needed a fourth, related fix.** Investigating whether `CssLayoutEngineFlex`/
`CssLayoutEngineGrid` could make `IsHeightDefinite` return a false negative turned up a real, previously
unrecorded gap: neither engine ever calls `ApplyHeight`/touches `IsHeightCalculated` at all — both only
ever reflect a resolved stretch/main-size height in the item's own `Height` CSS string *transiently*
(set it, re-lay the item out, revert it immediately). A percentage-height descendant resolved during
that one transient window worked; anything resolving afterward — including the container's own later
`ApplyParentHeight` sweep, which re-reads the by-then-reverted string and would otherwise correctly
recompute a false answer — saw a false "indefinite." Added `CssBox.AlgorithmicDefiniteHeight`, written
at the three points these engines already compute the real value (`CssLayoutEngineFlex`'s main-axis
resolution and cross-axis stretch, `CssLayoutEngineGrid`'s `align-self: stretch`), reset every layout
pass before each decides whether it applies. Wired into `IsHeightDefinite`/`ResolveDefiniteHeightValue`
*before* the independent-formatting-context carve-out, not after: a flex/grid item's own algorithm is
authoritative and isn't grown further by a contained float the way ordinary auto-height block sizing
is, even though a flex/grid item is itself always an independent-formatting-context box.

## What was deliberately not done

- `ApplyParentHeight`'s own second `ApplyHeight` re-run is now redundant for percentage-gating
  purposes at every site upgraded here, but it wasn't removed or simplified — it's still a prerequisite
  for `ResolveAbsolutelyPositionedDescendantAutoBlockMargins`, a different concern. Named explicitly as
  a follow-up opportunity, not folded into this change.
- `CreateVerticalLineBoxes`'s own definite-height check (a documented pre-existing workaround for the
  same old-flag timing problem, in a completely different subsystem) was left alone — its own
  `DefiniteContentHeight` helper doesn't gate on `IsHeightDefinite` at all today, and giving it that
  benefit needs its own care, not a drive-by change here.
- Verified whether this refactor also closes #807 (`cqh`/`cqb` against a percentage-height container
  whose own ancestor isn't yet settled) — a synthetic `CssBox`-tree test for it failed (0 instead of the
  expected resolved value), most likely because the minimal test tree didn't wire `ContainingBlock`
  resolution the way a real layout does, not because the underlying logic is wrong (every other
  percentage-chain test, run through real HTML layout, passed). Not confirmed either way; #807's own
  accepted-gap file was left untouched rather than closed on unverified evidence.

## Evidence

- New/updated tests: `InlineBlockDeclaredHeightGeometryTests` (anchor-specific growth-direction tests,
  a wrap-clears-the-tall-box regression test for #1166, a real percentage-height-resolves test replacing
  the old pinned-to-broken-behavior one, a still-indefinite-ancestor no-op test, a percentage-height
  line-reservation test), `InlineBlockOverflowClipPaintTests` (two new paint-level clip-stream tests:
  `vertical-align: bottom` anchor growth, percentage height), `HeightDefinitenessTests` (new file, direct
  coverage of `IsHeightDefinite`/`ResolveDefiniteHeightValue` including the min/max-clamp and
  table-cell/float-IFC carve-out cases), `FlexGridAlgorithmicDefiniteHeightTests` (new file, row/column
  flex and grid stretch percentage-height resolution, explicit-height/non-stretch null guards, a
  repeated-layout staleness guard).
- Full suite green after each staged commit (12,000+ tests), including both Acid2 regression tests
  named as depending on the old flag's exact behavior.
- `dotnet build PeachPDF.slnx -t:Rebuild`: zero warnings, twice (after the `IsHeightDefinite` refactor
  and again after the flex/grid fix).
