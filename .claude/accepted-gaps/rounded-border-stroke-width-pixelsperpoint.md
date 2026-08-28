# Border stroke width ignores non-default `PixelsPerInch`

`BordersDrawHandler.GetWidth(Border, CssBox)` returns `box.ActualBorderTopWidth`/`ActualBorderLeftWidth`/
etc. directly - an internal, `PixelsPerInch`-inflated layout-space value, the same space `CssBox` geometry
lives in throughout layout. `GetPen(RGraphics g, LineStyle, RColor, double width)` assigns that raw value
straight to `RPen.Width` with no division by `g.PixelsPerPoint`, unlike every draw *coordinate* in this
file and in `GraphicsAdapter.cs`, which are all correctly divided before reaching the backend.

At the library's default `PixelsPerInch = 72` (`PixelsPerPoint = 1.0`) this is invisible - dividing by 1.0
is a no-op. At any other value, every border - straight or rounded - renders visibly thicker than its
declared width, proportionally to `PixelsPerInch / 72`, while its *position* stays correct (that part was
fixed for [#812](https://github.com/jhaygood86/PeachPDF/issues/812)'s rounded-path clip/border/background
position-and-radius scaling; the pen's own stroke *width* is a separate, independent value this fix
deliberately left alone to keep that PR's scope to what #812 actually reported).

Confirmed visually in the `border_radius_96dpi` TestHarness showcase (added alongside the #812 fix): every
swatch's `border: 2px solid` renders noticeably bolder at `PixelsPerInch = 96` than at the default 72,
while every rounded curve's position/size and every overflow-clip boundary render identically at both.

Tracked as [issue #851](https://github.com/jhaygood86/PeachPDF/issues/851); fix sketch there is a one-line
`width / g.PixelsPerPoint` division in `GetPen` (or at `GetWidth`'s call sites), mirroring the correction
already applied to border/background/overflow-clip path coordinates.
