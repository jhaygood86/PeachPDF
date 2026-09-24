# A positioned box inside a non-positioned stacking context keeps an overflow clip it should escape (#1315)

**Gap:** CSS Overflow 3 §3 clips only descendants whose containing block chain passes through the
clipping box. `relative > overflow:hidden > opacity:.5 > absolute` should leave the absolute box unclipped,
because `opacity` forms a stacking context but not a containing block. PeachPDF still clips it to the
`overflow: hidden` box. The same applies to `mix-blend-mode` and to a flex item with `z-index` in the
middle position, and to `position: fixed` in the same place.

**Why:** the positioned box is a stacking participant of the `opacity` box, so it is painted inside that
box's `FragmentPainter.PaintBoxContent`. That call pushes its own `OverflowClip` (the overflow box's padding
box, which is correct for the `opacity` box itself) and keeps it active across its stacking layers. On top
of that, `PaintWithOpacity` sizes and clips the offscreen tile to the clip active at the time. The
participant's own `OverflowClip` being null (#1314) cannot undo a clip that is already on the stack.

**Why it was left:** closing it means painting a stacking context's hoisted participants outside its own
overflow clip, and sizing the opacity/blend tile to what it contains rather than to the current clip. That
is a paint-structure change well beyond #1314's containing-block fix.

**Measured:** a probe recording the active clips when the absolute box painted showed two
`{W=100,H=5}` clips with the middle box set to `opacity:0.5` or `mix-blend-mode:multiply`, and none
without it.
