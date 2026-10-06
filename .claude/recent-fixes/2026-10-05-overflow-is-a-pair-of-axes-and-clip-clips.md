# `overflow` is a pair of axes, and `clip` clips

_css-overflow-3 §3. Tracker: #1628._

`overflow: clip` failed to parse (`Map.OverflowModes` had no `clip`) and `overflow-x`/`overflow-y` had a name
in `PropertyNames` but no property, so neither clipped. Chrome clips both to the box.

- **Model.** `overflow-x` and `overflow-y` are the stored longhands (`css-properties.json`); `overflow` is a
  shorthand with `Periodic`, so one or two values work. `CssBox.Overflow` stays as the **used** value read off
  both axes (visible beside a scroll container is auto, clip is hidden), so its ~15 readers (BFC, monolith,
  scroll-container tests) are unchanged. A box whose axes are only visible/clip reports `visible` there and
  is clipped through `CssBox.ClipsWithoutScrolling`, read only by `DomUtils.ClipsItsOverflow` and
  `RenderUtils.TryPushOverflowClip`. That keeps `clip` out of every `!= Visible` test, which is the point of it:
  no scroll container, no formatting context, no monolith.
- **Trap.** The registry is longhand-only, so `CssUtils.Set(box, "overflow", ...)` no longer exists;
  `ContainerBuilder.ClampLines` sets both axes. A test or caller using the string `"overflow"` now needs
  `overflow-x`/`overflow-y`.
- **Left alone.** `auto` and `scroll` still do not clip in PDF output (only hidden/clip do). `clip` on one axis
  beside `visible` clips both axes; browsers clip only the one. Tracked in #1635.
- **Evidence.** `OverflowAxisClipTests` paints through `RecordingGraphics` and checks the visible width of an
  overflowing word (about 40% for each clipping form, whole for `visible`), the shorthand expansion, and the
  scroll-container rule per axis combination. CLI render of the issue's document through pymupdf shows all
  three boxes cut at the edge.
