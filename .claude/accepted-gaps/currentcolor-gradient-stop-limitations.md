# `currentcolor` as a gradient stop: what is not covered

A gradient stop written exactly `currentcolor` takes the painting box's own `color` when the gradient is built (see
[the recent fix](../recent-fixes/2026-10-15-currentcolor-gradient-stop.md)). Two cases of valid CSS still go wrong; both are
tracked in #1760.

- **`currentcolor` inside a colour function in a stop is not resolved.** `color-mix(in srgb, currentcolor, red)` paints black and
  `rgb(from currentcolor r g b)` paints transparent. The render parsers read a stop's text with `GetActualColor`, which has no box, so
  only a stop that is exactly the keyword is recorded (`CurrentColorStops`) for late resolution. Fixing it means resolving the keyword
  inside the stop's text before `GetActualColor` sees it (the parse has no box; it would need the same late binding for a whole colour
  expression, not an index).
- **A page-margin box (and `@page`) with no `color` of its own takes black, not the inherited `color`.** css-page-3 has the margin boxes
  inherit from the page context and the page context from the root element. PeachPDF uses only the declaration's own `color`, else black -
  as does that box's text (`MarginBoxRenderer.BuildBrush`, which reads only `rgb()` and hex, where the gradient's colour goes through the full
  colour parser), so neither agrees with a browser. Fixing it means inheriting `color` through the page and margin-box cascade for text and gradients alike.
- **A gradient propagated to the canvas from `<body>` resolves `currentcolor` against the body, not the root element.** `body { color: green; background:
  linear-gradient(currentcolor, red) }` with no `html` background paints green to red here; a browser resolves it against the root element's colour
  (black), because the background is the canvas's.
- **A single-stop `linear-gradient(currentcolor)` paints nothing,** where a browser paints that colour solid (a single literal stop already does the same
  here). The declaration now survives the CSS-OM, so it also wins over a fallback such as `background: #fcc` that used to show because the declaration
  was dropped.
- **A gradient `list-style-image` marker is larger than a browser's** (about 40px against 12px for `linear-gradient(currentcolor, red)`): it is sized as a
  literal gradient marker already is here. Not specific to `currentcolor`.
