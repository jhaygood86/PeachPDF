# Real margin collapse for vertical-writing-mode block-axis stacking

_Landed 2026-08-20._

[Issue #776](https://github.com/jhaygood86/PeachPDF/issues/776): `CssBox.LayoutVerticalBlockChildren`
(added for #760) summed adjoining physical margins between block-axis-stacked siblings of a
`vertical-rl`/`vertical-lr` box instead of performing real CSS 2.1 §8.3.1 adjoining-margin collapse (max
of the positives plus the min of the negatives, with self-collapsing empty boxes folding through). Closing
this also turned into closing the vertical box's own block-start/block-end edge collapsing with its
first/last stacked child, and fixing a related latent bug in the shared `horizontal-tb` cluster — see
below for why the scope grew from the issue's own text.

**The right design was a genuine generalization of the existing `CollapsedMarginBefore`/`MarginBottomCollapse`
cluster to be writing-mode-aware, not a second, parallel implementation for the vertical axis.**
`FoldOwnAdjoiningTopMargins` is now `FoldOwnAdjoiningBlockStartMargins(ref margins, PhysicalSide side)` —
`side` is supplied by the *caller's own orchestration*, not re-derived per box: `CollapsedMarginBefore`
always passes `PhysicalSide.Top` (ordinary block flow always collapses physical top/bottom margins,
regardless of any individual child's own `writing-mode` — `margin-top` is a plain physical property, not
a logical one), while `LayoutVerticalBlockChildren` passes whichever side its own
`WritingModeFrame.BlockStartIsRight` names. Getting this backwards (deriving the axis from *each box's
own* `WritingMode` instead) was the first real bug found while implementing this: it silently used a
vertical box's own internal axis (e.g. physical right) when folding its margin into an *ordinary*
`horizontal-tb` parent's chain, where only the physical-top value is ever meaningful — caught by a test
asserting an actual numeric position, not just that layout completed. `_groupTopMarginOverride`/
`TryTakeGroupTopMarginOverride`/`CollapsedMarginTop` were renamed to their `BlockStart` equivalents
(`CollapsedMarginTop` is write-only — no read site anywhere in the repo, confirmed by grep — so the rename
was safe).

**A chain must stop descending the instant adjacent boxes' `WritingMode` values differ — but the child's
own margin on the caller's axis still folds in first,** and a second, easy-to-miss guard is needed at the
method's own entry: if `LogicalPropertyResolver.BlockStart(this.WritingMode) != side`, `this` is *already*
the far side of a writing-mode boundary from the caller (e.g. a `vertical-rl` box positioned by an
ordinary `horizontal-tb` parent's always-`Top` axis) and must not be descended into at all, even though
none of the loop's other conditions (`Overflow`, border, padding) would catch that on their own — this was
the second real bug, caught the same way (a test expecting a `vertical-rl` wrapper's own first child to
sit flush kept getting a raw, uncollapsed margin instead, because nothing had ever set its override).

**Box-own-edge collapse only has anything to collapse against when the *outer* context shares the same
block axis** — nested `vertical-rl`/`vertical-lr` composition, not a vertical box embedded directly in
ordinary `horizontal-tb` flow. An outer `horizontal-tb` parent's own top/bottom stacking axis is simply
*unrelated* to a vertical child's internal left/right one — there's no adjoining relationship to collapse
in the first place, not a blocked one. Two test cases built on the opposite premise (asserting a
standalone vertical wrapper's first/last child sits flush against the wrapper's own edge) failed with the
*old*, uncollapsed value even after the fix was otherwise correct — the fix was to the tests, not the
code, once traced through by hand: a standalone wrapper's first child positions itself using its own raw
(possibly chain-extended) margin, exactly as it always did, since there is no ancestor override to receive.

**A vertical box's own auto-width self-collapsing-empty-child check (`IsBlockAxisMarginCollapseThrough`)
is a new method, not a reuse of `IsMarginCollapseThrough`, because of a genuine engine asymmetry**:
`width: auto` on a vertical box's child *stretches* to fill available block-axis space
(`CssLayoutEngine.GetBoxWidth`), unlike `height: auto`, which always shrink-wraps in this engine — so
gating "is this child empty" on the `Width` style token the way `IsMarginCollapseThrough` gates on
`Height == auto` would almost never fire. Gating on the child's own *already-resolved* extent instead
works because every call site (mirroring `IsMarginCollapseThrough`'s own call sites) only ever asks about
an already-laid-out box.

**A third bug, initially missed and caught only by hand-tracing a failing test's exact numbers:** the
sibling-loop's own bookkeeping added a self-collapsing child's leading margin group to
`logicalBlockOffset` on *every* iteration of a self-collapsing run, rather than deferring it until the
group is finally "spent" by the next real child (or the box's own trailing edge) — a two-member
self-collapsing run doubled the expected gap (20 became 40) because the same collapsed value got added
twice. The fix mirrors `FoldMarginsPrecedingChild`'s own backward-walk-to-the-real-anchor shape, just
restated forward: a self-collapsing child's own `startMargin` is used only for its (visually irrelevant,
since it has ~0 width) placeholder position, and `logicalBlockOffset` only actually advances by that
margin once, when a non-self-collapsing child (or the trailing-edge fold) resolves the run.

**The block-end (`FoldOwnTrailingBlockMargin`) mirror of `MarginBottomCollapse`'s "is this box its own
parent's last child" gate is ported structurally as-is, including a condition
(`!(ParentBox.BlockEndMargin < 0.1)`, mirroring `MarginBottomCollapse`'s own
`!(_parentBox!.ActualMarginBottom < 0.1)`) whose exact purpose wasn't fully re-derived from first
principles during this change.** `MarginBottomCollapse`'s own extensive comment explains the "is last
child" condition's own double-count-avoidance reasoning in detail, but not this specific sibling
condition; rather than guess and risk diverging from already-shipped, tested behavior, this fix preserves
the exact same structural shape, axis-mapped, so whatever correctness property (or accepted limitation)
the horizontal version already has is inherited unchanged rather than silently altered.

**A post-change review (8 independent finder passes) surfaced two more real gaps, both fixed, plus one
plausible-sounding "fix" that turned out to be wrong on closer inspection — worth recording since the
wrong one would have been an easy mistake to leave in.**

- **Fixed: the self-collapsing branch only folded a self-collapsing child's own single trailing margin
  (`ownEndMargin`), not its whole self-collapsing subtree.** `FoldOwnAdjoiningBlockStartMargins`'s own
  leading-side chain walk only follows a box's *first* in-flow child at each level, so a self-collapsing
  wrapper with a *second* self-collapsing sibling child never had that second child's margin folded in by
  anything — mirroring `FoldSelfCollapsingMargins`'s existing full-subtree walk (every in-flow descendant,
  not just a first-child chain) closes this via a new `FoldSelfCollapsingBlockMargins`, called instead of
  the bare `ownEndMargin` fold once a child is confirmed self-collapsing. Caught by review, confirmed with
  a new test (`VerticalRl_SelfCollapsingWrapperWithTwoSelfCollapsingSiblingChildren_BothChildrensMarginsJoinTheGroup`)
  using a *smaller* margin on the first (chain-reachable) sibling and a *larger* one on the second — the
  bug was invisible whenever the first sibling's margin happened to dominate, which is why the earlier,
  single-nested-child test in this same PR didn't catch it.
- **Considered and reverted: gating `MarginBottomCollapse` on `HasDifferentWritingModeFromParent`, the way
  `FoldOwnTrailingBlockMargin` needed to be gated.** Plausible by analogy (review flagged it, reachable via
  an orthogonal `horizontal-tb` child stacked as a vertical box's own last child), but wrong on inspection:
  `MarginBottomCollapse` operating on a box's own bottom margin collapsing with *its own* last in-flow
  child is a relationship entirely internal to that box and its own descendant, governed by the box's own
  `writing-mode` alone — unlike the leading-chain-walk bug this fix is modeled on, which came from applying
  ONE frame's fixed axis to a *different* box's own subtree. A vertical `ParentBox`'s own `ActualMarginBottom`
  is also not a double-count risk the way an ordinary `horizontal-tb` parent's is, since
  `LayoutVerticalBlockChildren`'s own stacking loop reads a child's left/right margins for sibling gaps,
  never top/bottom. Left unfixed, with a comment at the call site recording this reasoning so the same
  question doesn't get re-raised without it.

**Two lower-severity findings were left as known limitations rather than risking a late, hasty fix:**

- **O(N²) cost for a chain of N nested vertical-rl/vertical-lr wrappers each the sole/first child of the
  next.** `LayoutVerticalBlockChildren` calls `FoldOwnAdjoiningBlockStartMargins` on every child
  unconditionally (needed so a self-collapsing child's *true* margin — not just its ancestor-assigned
  override placeholder — is available for the next sibling to fold into, see the second bug above), unlike
  `CollapsedMarginBefore`'s own short-circuit-on-override-first shape for ordinary flow. Real, but only
  matters for real-world documents with many levels of vertical-in-vertical wrapping, a narrow case; fixing
  it safely would need deferring the walk until self-collapse status is known, adding real complexity this
  late in an already large change.
- **A captioned `<table>`'s synthetic grid-decoration box** (`TableGridDecorationBox`, #721) never has its
  `WritingMode` updated to match a `vertical-rl`/`vertical-lr` table (`AdoptBorderAndBackgroundFrom` only
  copies Border/Background style areas, not Text) — the new `HasDifferentWritingModeFromParent` guard in
  `IsMarginCollapseThrough`'s recursive descent can therefore spuriously treat a borderless, captioned
  vertical table as not margin-collapse-through when it otherwise would be. Narrow (borderless + captioned
  + vertical + margin-collapse-through mattering to a following sibling) and adjacent to the already-tracked
  `#762` vertical-table-caption gap rather than something this PR's own scope should absorb.

New tests: `VerticalWritingModeLayoutIntegrationTests.cs` (sibling-to-sibling collapse including
mixed-sign margins and both-negative margins, a self-collapsing middle child folding into one shared
group with its neighbors, nested self-collapsing children, two self-collapsing SIBLING children inside one
self-collapsing wrapper, box-own-edge collapse on both the leading and trailing side for nested
vertical-in-vertical composition, and the writing-mode-boundary fix stopping at a mirrored
`vertical-rl`/`vertical-lr` pairing) and `CollapsedMarginBeforeTests.cs` (the writing-mode latent-bug fix
for an ordinary `horizontal-tb` box whose first child is `vertical-rl`). A new TestHarness showcase section
(9c, in the existing `writing_mode` showcase) visually demonstrates both sibling-to-sibling and
box-own-edge collapse, rasterized and checked by eye. Full net8.0 suite (9022 passing), zero-warning
`dotnet build -t:Rebuild`, and ~97% diff coverage on the changed lines (the only gaps: the pre-existing
write-only `CollapsedBlockStartMargin`/`CollapsedMarginTop` property's getter, never read anywhere in the
repo, and two defensive switch arms for a `PhysicalSide.Bottom` value never actually passed by any of this
change's own call sites).
