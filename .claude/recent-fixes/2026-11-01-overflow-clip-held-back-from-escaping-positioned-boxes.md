# A positioned box in a non-positioned stacking context escapes an overflow clip it is not inside (#1315)

CSS Overflow 3 §3 clips only descendants whose containing-block chain passes through the clipping box.
`relative > overflow:hidden > opacity:.5 > absolute` must leave the absolute box unclipped. #1314 gave the
absolute box a null `OverflowClip`, but clips are canvas state that every frame above inherits, and two of
them reached it anyway: `PaintStackingParticipant` pushed the overflow box's clip around the hoisted
`opacity` box's whole paint (and so around the layer's composite), and the `opacity` box's own
`PaintBoxContent` pushed it again around its layers. Lifting only the second changes nothing for the repro.

## The fix

A clip whose clipping box some descendant escapes is **held back** instead of pushed, and each box that
paints pushes the held clips its own chain passes through (`FragmentPainter.Clips.cs`):

- `PaintStackingParticipant` resolves each hoisted ancestor's clip (`RenderUtils.ResolveOverflowClip`,
  split out of `TryPushOverflowClip`) plus any already-held ones. An entry the participant's subtree
  escapes (`HasEscapingDescendant`: an out-of-flow box off that clipper's chain, memoised per fragment) stays
  **pending**; the rest are pushed around it as before. The participant's own `PaintBoxContent` then pushes
  the pending ones *inside* itself (`PushClipsFor`), so for an opacity/blend group they go inside the layer
  and its composite is not clipped.
- `PaintBoxContent` lifts its own clip off the canvas for the duration of its layers when a participant
  escapes it (`EntriesEscapedBy`), holding it for them; each participant re-pushes it if on chain. The
  raised (z-index > 0) layers do the same instead of pushing the box's clip around all of them.
- `DomUtils.ClippingBoxOf` names the box a fragment's own `OverflowClip` belongs to. A clip that is only a
  fragmentainer band has no box and is still pushed around everything.

The common case (no out-of-flow box under a clip) only pays a position check per participant.

## Traps

- Check the group, not just the box: the canvas clip active when `BeginLayer` composites is what clips the
  whole group, so the clip must be absent *outside* the layer and present *inside* it.
- Secondary painters (`_ownOnly` transparency probe, the `_textOnly` overlay) copy `_deferredClips`; the
  backdrop/flatten mirrors start from the page root and re-derive them.
- Not covered: a clip only reachable through an ancestor that is not a hoisted participant's
  `ClipAncestors` (plain nested boxes between are participants themselves, so they are covered).

## Evidence

`OverflowClipIntegrationTests.PaintingAChildOfAStackingContext_...` (opacity, mix-blend-mode, fixed,
in-flow, positioned middle, z-index child); engine 17040 and `PeachPDF.Tests` green on net8.0. The repro
(opacity and mix-blend-mode groups inside `overflow:hidden`, plus an in-flow control) rasterized through
PDFium and MuPDF: the absolute boxes are drawn whole at the right opacity, the in-flow box stays clipped.
