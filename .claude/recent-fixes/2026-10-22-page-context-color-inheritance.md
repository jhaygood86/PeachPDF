# Page and page-margin boxes inherit `color` (#1904)

A margin box used only its own declared `color` (else black) for its text (`BuildColor`), its border's `currentcolor` and the `currentcolor` of a gradient (`LayeredBackgroundPainter.OwnColor`); the page box's own border and gradient did the same. css-page-3 has the margin boxes inherit from the page context, which inherits from the root element.

Fix: one resolver, `MarginBoxRenderer.ResolveColor(style, pageStyle, container, renderContext)` - own, else `@page`'s, else the `html` box's `ActualColor` - used by text, border and gradient alike, and passed into `LayeredBackgroundPainter.PaintAsync`/`PaintBorder` as an explicit colour (`OwnColor` stays as the fallback for callers without one). The page box passes a null page style. Text now parses through `GetActualColor` like the other two (the old `ParseColor` read only `rgb()` and hex).

Trap: `CurrentColorGradientStopTests` used to pin the old black behaviour for a page background; it now expects the root's colour.
