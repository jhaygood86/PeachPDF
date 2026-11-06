# Scroll container gaps closed: sticky, reload, alignment, writing modes, transforms, text, fling

Follows [scroll snap, behavior and scrollbar properties](2026-11-06-scroll-snap-behavior-overscroll-scrollbars.md).

- **Sticky in a scroller.** `StickyFor` takes the nearest scroll container as the sticky view rectangle (port moved by the offset), and a box held by the container itself is bounded by the container's
  scrollable area, not its own padding box: a fixed-height scroller is the parent of the header, and without this the header could never leave 100pt of travel. The box is not "attached" (not painted in the
  viewport-attached layer), because that layer is above the container's clip. `SetScroll` calls `RefreshSticky` so the displacement follows every scroll, including a glide step and a restored reload.
- **Reload.** `ReloadAsync` takes the offsets by `HtmlTag.SourceOrdinal` before reparsing and restores them after layout, clamped. The first `IndexFor` of the new tree would otherwise drop every offset
  whose box is gone, so the offsets must be taken out before it runs.
- **Negative scroll range.** `ScrollState`/`ScrollMetrics` gain `MinX`/`MinY`; every clamp, thumb fraction, snap `Settle`, scroll timeline and WinUI percentage uses the range, not `0..Max`. A scroll
  timeline over right-to-left content has its start at offset 0 and progress along `-offset`.
- **Transforms** are applied to the border boxes, lines and words when the overflow area is measured, accumulating the transforms of the boxes between the container and the box. Shadows and outlines are
  ink overflow in CSS Overflow 3 and stay out, so the gap file no longer lists them.
- **`ScrollTextIntoView`** needs the owner box of a text run (`TextRun.Owner`, internal) and the inverse of the text frame (`Geometry.ContentLeftPt`, `MarginLeft`) to turn the drawn rectangle back into the
  undisplaced layout rectangle `ScrollIntoView` works in.
- **WinUI**: fling and track repeat run on `DispatcherQueueTimer`s and end on release, cancel, a new touch or the document going away; the maths is in `HtmlViewLogic` for unit tests.
- Not done: scrollbar-gutter (nothing to do for overlay bars), selection auto-scroll while dragging.
