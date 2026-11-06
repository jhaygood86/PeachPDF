# Scroll snap, smooth scrolling, overscroll and scrollbar properties on scroll containers

Follows [scroll containers](2026-11-06-scroll-containers.md). Sixteen properties were added to `css-properties.json` (`scroll-behavior`, `overscroll-behavior(-x/-y)`, `scroll-snap-type/align/stop`,
`scroll-margin*`, `scroll-padding*`, `scrollbar-width/color/gutter`) with text grammars in `CSS/ScrollGrammar.cs` and three shorthands registered with `AddShorthand`.

- **Every programmatic scroll goes through `ViewportState.Navigate`**: clamp, snap (unless a drag), then either set the offset or start a tween. `SetScrollPosition`, `ScrollBy` and `ScrollIntoView` take a
  `ScrollBehavior`; a thumb drag and a finger use `ScrollThumbTo`/`DragScrollBy`, which are exact, and the host calls `SettleScroll` on release. Snapping on every step of a drag made a `mandatory`
  container impossible to move by less than half a snap interval - it snapped back each time.
- **Tweens are stepped from `AdvanceAnimations`** (wrapper `WithScrollTweens`), reported as dirty regions, and keep `HasRunningAnimations` true. A new scroll replaces the running tween. Reduced
  motion jumps.
- **Overscroll**: `ScrollContainerAt` returns the first container able to take the turn, or one whose `overscroll-behavior` is `contain`/`none` on that axis; the WinUI view consumes the wheel whenever
  a container is returned, even if it is at its end and nothing moves. The test has to cover both - a unit test of the document alone cannot show the page being left alone.
- **Scrollbar width and colour** are read where the bars are made (`ScrollbarGeometry.Of`, `FragmentPainter.PaintScrollbars`), so the hit test and the painter agree.
- Tests: `ScrollSnapAndBehaviorTests` (15), `CssomLonghandBaseline` regenerated, `PublicApi.txt` regenerated. Not done: sticky inside a container, offsets kept across a reload (see the gap file).
