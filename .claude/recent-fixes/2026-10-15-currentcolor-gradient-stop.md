# `currentcolor` as a gradient stop colour

Tracked in #1759. A `background` with `currentcolor` in a gradient painted nothing - not because the render layer failed on it, but
because the CSS-OM grammar never let the declaration through.

## The load-bearing ideas

- **Two layers had to change.** `GradientConverter` validated a stop's colour with the plain `ColorConverter`, so the whole declaration
  (`background-image`, the `background` shorthand, `border-image-source`, and the registry validator `@supports` uses) was invalid.
  It now uses `ColorConverter.WithCurrentColor()`. Past that, `CssValueParser.ParseLinearGradient`/`Radial`/`Conic` read the stop's text
  with `GetActualColor`, which cannot resolve the keyword - and has no box to ask.
- **Resolve at paint, not at parse.** The setter parses `background-image` during the cascade, when `color` may not be final (a
  `color` declared after the gradient, an animated `color`). So the parser records which stops say `currentcolor`
  (`CurrentColorStops` on each parsed gradient, indexes into `Stops`; a two-position stop records both) and leaves a placeholder
  colour; `CssImagePainter.ResolveCurrentColorStops` gives them `box.ActualColor` when the gradient is built. The tuple shape of
  `Stops` was deliberately left alone: sixty tests construct stops with it.
- **The conic brush had no box.** `GetConicGradientBrush` took only the rect, unlike the linear and radial ones; it gained an optional
  `box` (both call sites pass it, `CssImagePainter.Paint` and `BorderImageDrawHandler`). Without a box a `currentcolor` stop is black.
  The first version of the tests missed a mutant that dropped the box at the `Paint` call - the builders were tested directly, the
  entry every layer goes through was not; `TheLayerPath_PaintsTheStopInTheBoxsColor` closes it.
- **A page and a page-margin box paint with the root box as geometry stand-in**, so `box.ActualColor` would be the root element's colour, not the
  box's own: a margin box's text uses its `color` or black (`BuildBrush`), and its gradient must say the same. `Paint` and the three brush builders take an
  optional `currentColor`, which `LayeredBackgroundPainter` (the @page and margin-box backgrounds) and `MarginBoxRenderer.PaintImage` (a
  margin box's `content: <gradient>`) pass from the declaration's own `color`. The first review caught it: everything probed in the ordinary box tree was
  right, and these two were silently the root's colour.
- **The animation mixer needed nothing.** `CssValueInterpolator` already substitutes `currentcolor` tokens inside a gradient (it scans
  `linear-gradient(` as an open token); it was only unreachable because the value never survived the CSS-OM.

## Not done

`currentcolor` inside a colour function in a stop (`color-mix(in srgb, currentcolor, red)`, `rgb(from currentcolor ...)`) is not
resolved (it paints black or transparent); only the bare keyword is, and a margin box without its own `color` takes black, not the inherited
one - see [the accepted-gap note](../accepted-gaps/currentcolor-gradient-stop-limitations.md) (#1760). `content: <gradient>` on a `::before`, `list-style-image` and `background-clip: text` with a `currentcolor` stop render in the box's colour (probed by
hand, no dedicated test).
