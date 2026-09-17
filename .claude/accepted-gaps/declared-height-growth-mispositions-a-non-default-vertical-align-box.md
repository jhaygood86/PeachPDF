# A declared-height correction still mispositions an empty/overflow-hidden box under default baseline alignment

Tracked as **#1169**. `CssLayoutEngine.HeightenAtomicInlineRectangles` grows an inline-flowed
`inline-block`'s declared-height rectangle from whichever edge `AnchorOf` says
`ApplyVerticalAlignment` already anchored: `vertical-align: top`/`bottom`/`text-bottom`/`middle`
(and the default `baseline` on a box with real, `overflow: visible` content) all grow from the
correct edge now.

## What remains circular

The default `baseline` case on an **empty box, or one whose `overflow` isn't `visible`**, is still
left growing downward from its flow-assigned top, unconditionally — because that box has no
baseline of its own: `CssLayoutEngine.AtomicInlineBaselineOf` falls back to treating its bottom
margin edge as its baseline (`rect.Bottom + box.ActualMarginBottom`), read from inside
`ApplyVerticalAlignment`, using the box's still-natural (pre-growth) rectangle, before growth ever
runs. Anchoring this case correctly needs the grown height known *before* `ApplyVerticalAlignment`
computes that fallback baseline — which is a different, deeper problem than #1166's own line-sizing
gap (already closed): #1166's fix hooks into `FinalizeFlowBoxExit`, a completely separate code path
from `ApplyVerticalAlignment`'s own baseline-extent fold, specifically so it wouldn't need to touch
this. Feeding a grown rectangle into the fold itself, to fix *this* case, would still risk exactly
the previously-measured regression below.

An anchor-aware rewrite that also bottom-anchored this specific case was tried during #1169's own
implementation and reverted: it made the empty/`overflow`-non-`visible` case land at the wrong
position, because its own "anchor" was itself computed from the pre-growth rectangle — the same
shape of regression #1101's development history already recorded once. `HeightenAtomicInlineRectangles`
and `AnchorOf`'s own remarks record this reasoning at the code they apply to.

## What remains correct

Every keyword-anchored case (`top`/`bottom`/`text-bottom`/`middle`/`sub`/`super`/a length offset)
and the default `baseline` case on a box with real content grows from the correct edge, per
`InlineBlockDeclaredHeightGeometryTests`' anchor-specific tests and
`InlineBlockOverflowClipPaintTests`' paint-level regression test. Only the narrower empty/
`overflow`-non-`visible`-under-default-`baseline` shape remains open.
