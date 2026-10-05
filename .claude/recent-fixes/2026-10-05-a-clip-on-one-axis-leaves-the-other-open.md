# A clip on one axis leaves the other open

_css-overflow-3 §3.2. Tracker: #1635._

`overflow-x: clip` beside `overflow-y: visible` (or the reverse) clipped both axes, because the clip rectangle
was the padding box and `ClipsWithoutScrolling` was one flag. Browsers cut only the `clip` axis.

- **Fix.** `CssBox.ClipsOverflowHorizontally`/`ClipsOverflowVertically` say which axes cut; a scroll container
  cuts both. `RenderUtils.OpenUnclippedAxes` turns the padding rectangle into one open by ±1e6pt on the
  uncut axis, so every consumer still gets a single `Rect`. It is applied at the two places a clip rectangle is
  made from a padding edge: `FragmentEmitter.OverflowClipOf` and `RenderUtils.TryPushOverflowClip`.
- **Trap.** The painter re-snaps a fragment's clip from `OverflowClipBasis.PaddingBox` when
  `SnapBoxDecorationsToCssPixels` is on, which would silently close the open axis again. A one-axis clip
  therefore carries **no basis** and is left as laid out; do not give it one without opening the axes again.
- **Corners.** A one-axis clip drops `border-radius` rounding (a rounded clip has no meaning on an open edge).
- **Evidence.** `OverflowAxisClipTests.AClipOnOneAxis_LeavesTheOtherAxisOpen` paints through the recording canvas
  and checks the visible area of a word past each axis (two of three cases fail without the change).
