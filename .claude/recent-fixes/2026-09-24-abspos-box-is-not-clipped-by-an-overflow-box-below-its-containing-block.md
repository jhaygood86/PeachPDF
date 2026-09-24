# An absolutely positioned box is not clipped by an `overflow: hidden` box below its containing block

Closes #1314.

## What was wrong

Both overflow-clip resolvers walked `CssBox.ContainingBlock` (the nearest *block* ancestor) up from the
box being clipped: `FragmentEmitter.OverflowClipOf`/`ClipIsInsideTheDisplacedRun` and, for hoisted
stacking participants, `RenderUtils.PushAncestorOverflowClips` (which clipped by *every* ancestor it was
hoisted past). CSS Overflow 3 §3 clips only descendants whose containing block chain passes through the
clipping box, and an absolutely positioned box's containing block is its nearest positioned ancestor. So a
non-positioned `overflow: hidden` wrapper between an abspos box and its positioned containing block clipped
it anyway, which browsers don't do.

Found on a real page: `#header { position: relative }` > `#homelink { float: left; overflow: hidden }`
(its only children are abspos logos, so it lays out 4px tall, all padding) > `img { position: absolute }`.
The logo was clipped to the 3pt strip and showed up as a sliver.

## The fix

`DomUtils.ClippingContainingBlockOf(box)` returns the next box on a box's clipping chain:

- **`position: absolute`:** the nearest ancestor that is positioned *or* has a `transform`, `perspective`,
  `filter` or `backdrop-filter`.
- **`position: fixed`:** the nearest ancestor with one of those four, or **null** (the page is its
  containing block, so nothing in the document clips it).
- **Anything else:** the ordinary `ContainingBlock`.

`DomUtils.IsOnClippingChainOf` walks that chain. Both emitter walks use the new step and stop on null.
`PushAncestorOverflowClips` now takes the participant box and skips an ancestor that isn't on the
participant's chain.

## Traps

- **Layout and clip use different containing-block rules on purpose.** Layout still positions an abspos
  box against `GetNearestPositionedAncestor`, which ignores `transform`/`filter`/`perspective`. The clip
  chain does not ignore them. Otherwise a `overflow: hidden; transform: …` wrapper (carousels, cropped
  images) would stop clipping its abspos children, which it did before this change and does in browsers.
- **`position: fixed` was never unclipped by `SuspendClipping()`.** `FragmentPainter.PaintFragment`
  suspends the clip stack for a fixed box, but `PaintBoxContent` re-pushes the fragment's own
  `OverflowClip` right after. The emitter now simply gives a fixed box no `OverflowClip`.
- **Accepted gap:** a non-positioned stacking context between the clipping box and the positioned box
  still clips it. See
  [the gap file](../accepted-gaps/positioned-box-inside-non-positioned-stacking-context-keeps-an-escaped-overflow-clip.md).
- **`IsOnClippingChainOf` jumps only at an out-of-flow box.** Every other step goes to
  `EffectiveParentBox`, not `ContainingBlock`. Two measured reasons:
  - **Captions:** `CssBox.ContainingBlock` skips a `table-caption`, so an `overflow: hidden` caption's clip
    was dropped for a hoisted participant inside it (`HoistedBoxInAnOverflowHiddenCaption_IsStillClippedByTheCaption`).
  - **Repeating `<thead>`/`<tfoot>`:** these are detached (`ParentBox = null`, `DomParentBox` = the table),
    and `ContainingBlock` walks `ParentBox` only. So the clip of every `overflow: hidden` box around the
    table was dropped for a hoisted participant (relative/float/z-index) inside a repeated header
    (`HoistedBoxInARepeatingTableHeader_IsStillClipped_ByAnOverflowAncestorOfTheTable`).

  Both were found in review, and both tests fail with a `ContainingBlock` step.
- **The containing block can be a box `overflow` doesn't apply to.** The nearest positioned ancestor of
  an abspos box may be a non-atomic inline (`<span style="position:relative">`) or a table row.
  `CssBox.ContainingBlock` never landed there, so upstream never checked `overflow` on one. Walking the
  real containing block did, and it clipped the abspos box to the span's line fragment or the row's
  rectangle. `DomUtils.ClipsItsOverflow` excludes non-atomic inlines and table rows, row groups, columns
  and column groups, and all three clip checks use it
  (`AbsposFragment_IsNotClipped_ByAContainingBlockOverflowDoesNotApplyTo`).

## Evidence

- New tests in `PushAncestorOverflowClipsTests`:
  - An abspos box, and a static child of one, get no emitter clip and no hoisted clip past a
    non-positioned wrapper.
  - The clip still applies when the wrapper is positioned, or has a `transform`/`filter`/`perspective`
    (for both absolute and fixed).
  - A fixed box gets no clip at all.
- Two `StackingContextOrderingTests` fixtures were relying on the old behaviour (non-positioned
  `overflow:hidden` wrappers clipping an abspos child). They now make those wrappers `position:relative`,
  which is what makes them clip in a browser.
- Full `net8.0` suite green, `dotnet build PeachPDF.slnx -t:Rebuild` 0 warnings. BioVitaal header
  rasterized through PDFium and MuPDF, and the logo shows in both.
