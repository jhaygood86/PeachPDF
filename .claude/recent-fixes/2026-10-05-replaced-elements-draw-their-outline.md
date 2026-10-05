# Replaced elements draw their outline (#1301)

`ReplacedFragmentPainter.Paint` (img, object/video, inline svg, iframe, math) and
`FormFieldFragmentPainter` returned after background, borders and content, and never reached the generic
`PaintBoxContent` path that is the only place outline rectangles were collected. `outline` was
silently ignored on every replaced element, while `MaximumOutlineReach` still widened the page clip
for a ring that was never drawn.

The fix is `FragmentPainter.PaintReplacedOutline(g, box, rect)`: one `OutlineRect` over the principal
rectangle with all four edges real (a replaced element is monolithic), handed to `PaintOrDeferOutline` so it
follows the same scope order as every other outline. Two traps:

- A deferred outline replays the clips recorded in `_overflowClips`, so the replaced painters now call
  `PushOverflowClip`/`PopOverflowClip` around the outline as `PaintBoxContent` does (both became
  `internal`). Without it a replaced element inside `overflow: hidden` would draw its ring unclipped once
  deferred.
- `_stopped` (a backdrop repaint that stops before this element's content) must skip the outline, since the
  outline is after the content.

An inline replaced element is an atomic inline and so its own outline scope: its ring is drawn as soon as
the element has painted, not after a following block. That matches how inline-blocks already behave, and is
why the tests assert the ring lands after the element's own background rather than after a sibling's.
