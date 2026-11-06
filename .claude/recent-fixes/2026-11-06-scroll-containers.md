# Scroll containers on a screen

**What it is.**
- `DomUtils.IsScrollerOnScreen` says what a scroll container is: a continuous document, `overflow` auto/scroll/hidden (the used value, so `clip` is not one), not the viewport's propagation source
  (`MonolithicContent.IsScrollContainer`), `overflow` applying to the box, and not a `<textarea>`/`<select>`/`<input>`. `ClipsItsOverflow` and `RenderUtils.ResolveOverflowClip` include it, so
  `auto`/`scroll` clip on a screen exactly as `hidden` does (and not in print, where they never have).
- `ViewportState` (now `partial`, the scroll half in `ViewportState.Scroll.cs`) keeps the offset of each container and a per-layout `ScrollIndex` (the container's fragment, scrollport and the extent of what
  it holds). The offset is a *displacement of the content*, not of the container: `DisplacementOf(box)` subtracts the offset of every container that holds a box on the way up (`ScrollParentOf`: the clipping
  block for an in-flow box, the containing block for an absolutely positioned one, none for fixed), so `GetElementBounds`, hit testing, text runs, links and form fields follow with no change of their own.
- Paint: a fragment whose scroll parent is scrolled pushes the shift at its entry (`TryGetEntryShift`), like a fixed or sticky shift; the clip it carries (its clipper's padding box, in the clipper's frame) is moved
  back by the sum of the offsets from the clipper down (`ClipShiftFor`, in `PushClipsFor`). `FragmentPainter.Scrollbars.cs` draws the overlay bars last, inside the container's own clips.
- Public: `IsScrollContainer`, `GetScrollMetrics`, `SetScrollPosition`, `ScrollBy`, `ScrollIntoView`, `GetScrollContainerAt` (the innermost container under a point that can scroll the way asked, for chaining), and
  `ScrollPositionChanged`. The WinUI view routes the wheel through `GetScrollContainerAt`/`ScrollBy` before the text area and the page.
- Timelines: `ResolvedTimeline.Scroller` is the container a scroll or view timeline follows (`scroll(nearest|self|root)`, a named `scroll-timeline` on a container, `view()` against the nearest container);
  `AnimationClock.RawScrollProgress` reads its offset, scrollport and range, and a view subject is measured from the scrollport's edge. A scroll calls `NotifyViewportChanged`, so the host steps once.

**Traps found by running it.**
- `Canvas.GetClip()` is the *latest rectangle pushed*, in the frame it was pushed in: not an intersection, and not adjusted for later transforms. Under a shifted frame it is in another coordinate system than
  the fragments, so culling against it hid the content that scrolled in (nothing showed in a nested container). Inside a carried frame (`_scrollFrames`) the painter does not cull (`ClipForCulling`), and
  `RenderUtils.ClipGraphicsByOverflow` does not intersect with `GetClip()`; the canvas applies the real clip in device space either way.
- The extent of what a container holds must stop at a nested container (its own box counts), or the outer one is scrollable by the size of the inner one's content.
- A container's own scroll must not move its own decorations: the shift is pushed by its *children* (their entry), never by the container; the container is moved only by the container around it.
- A page has a 20pt margin by default (`html` at 20,20 in the probes): a pixel assertion on a bare coordinate misses the box. Take the position from `GetElementBounds`/`GetScrollMetrics`.

**Not done.** See [the scroll container gaps](../accepted-gaps/scroll-container-gaps.md): touch, keyboard and scrollbar dragging, smooth scrolling, overscroll, snap, scrollbar properties, sticky inside a container.
