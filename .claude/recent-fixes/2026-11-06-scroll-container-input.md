# Input for scroll containers in the WinUI view

**What it is.**
- `ScrollbarGeometry` (engine, `Html/Core`) is the one place the overlay bars are laid out; the painter draws from it and `ViewportState.ScrollbarAt` hit-tests with it (deepest container first, through the
  scrollports of the containers around it). `HtmlDocument` exposes `GetScrollbarAt` (container, axis, thumb/track-before/track-after, track and thumb in points), `ScrollThumbTo` (the thumb's leading edge to
  a position in points, kept on its track, as a fraction of the scrollable distance), `ScrollByPage` (7/8 of the scrollport), `GetScrollContainerFor` (the nearest container around an element that can go
  the way asked, itself first) and `GetElementOf(node)`.
- `HtmlView.Scrolling.cs` is the host side: a press on a bar takes the thumb (`_scrollbarDrag` keeps how far from its start the pointer took hold) or pages the container; `OnPointerMoved` feeds the drag
  before anything else; `HandleScrollKey` runs after the edit keys, for the focused element's container; `AfterFocusChange` scrolls the containers around the new focus (`ScrollIntoView` on its element)
  and takes the accessibility tree again before `BringIntoView` reads its bounds; a touch over a container that can scroll some way remembers where it went down and, once it has moved 3 points, scrolls
  `GetScrollContainerAt(start, dx, dy)` by the distance and turns the view's own scroller off for the gesture (`RestoreTouchPolicy` puts it back); the node peers implement `IScrollProvider` over the same
  calls.

**Load-bearing.**
- A thumb drag converts the pointer to a position along the track, not a delta, so the thumb stays under the pointer when the container is clamped at an end and the pointer keeps going.
- The touch gesture is tied to the container under where it went down, not under the finger now: a finger that leaves the container while dragging keeps scrolling it, as a captured pointer does.
- Pages are 7/8 of the scrollport and an arrow step is 40 points, as in a browser; both are in points (a 100 px container is 75 pt, so a page is 65.6).

**Not done.** See [the scroll container gaps](../accepted-gaps/scroll-container-gaps.md): momentum, press-and-hold repeat, the pointer over a bar, a plain `div` in UI Automation.
