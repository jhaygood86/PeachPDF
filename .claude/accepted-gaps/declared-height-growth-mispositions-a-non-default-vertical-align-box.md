# A declared-height correction mispositions an inline-flowed inline-block under a non-default vertical-align

Tracked as **#1169**. Left in place by #1101, which made a declared `height`/`min-height` grow an
inline-flowed `inline-block`'s painted rectangle downward from its already-aligned top.

That growth direction is exactly right when the box's own baseline came from its first word's ascent
(an `overflow: visible` box holding text) or when `vertical-align: top` is declared — both anchor the
box's top regardless of its height. It is not right for `vertical-align: bottom`/`middle`/`sub`/
`super`/`text-top`/`text-bottom`/a length offset, or the default `baseline` on a box whose own bottom
margin edge stood in for its baseline (an empty box, or one whose `overflow` isn't `visible`) — each
of those anchors a *different* edge, and growing down from the (unchanged) top moves the box's real
position past where alignment placed it.

## Why it stays out

The keyword-anchored cases (`bottom`, `middle`, `text-bottom`) are individually fixable by growing
from the correct edge instead. The harder case — the default `baseline` on an empty or
`overflow`-non-`visible` box — is circular: that edge's position is *computed* by
`ApplyVerticalAlignment` from the box's still-natural (pre-growth) rectangle
(`CssLayoutEngine.AtomicInlineBaselineOf`'s `rect.Bottom + ActualMarginBottom` branch), so anchoring
it correctly needs the grown height known *before* alignment runs — which reopens
[the still-open "sizes the line" gap](declared-height-on-an-inline-flowed-inline-block-does-not-size-the-line.md)
(#1166): growing before alignment feeds the inflated rectangle into `ApplyVerticalAlignment`'s own
CSS 2.1 §10.8.1 baseline-extent fold and genuinely grows the line.

An anchor-aware version was implemented and tested while fixing #1101: it corrected the
keyword-anchored cases but produced a *worse* result for the empty-box/`overflow`-non-`visible` case
(its own "anchor" was itself derived from the wrong, pre-growth baseline) — exactly the shape #1101's
primary repro uses. Rather than trade one wrong direction for another in the still-circular case,
growth stayed simple (always downward), which is exactly right for the common case (text content,
default alignment) #1101's own issue text uses throughout.

## What remains correct

Every shape #1101's own issue and tests cover — text content or an empty box under the default
`vertical-align: baseline`, and `vertical-align: top` — grows correctly. Only an explicit non-default
`vertical-align` combined with a declared height taller than natural content is affected.
