# `currentcolor` in a gradient propagated from `<body>` (#1905)

`CssImagePainter` resolves a gradient's `currentcolor` as `currentColor ?? box.ActualColor`, and the canvas background is painted with `box` = the box that holds the background, so a gradient propagated from `<body>` read the body's `color`. The background belongs to the canvas, which takes the root element's properties (css-backgrounds-3 §2.11.2), so browsers use the root's.

Fix: `FragmentPainter.PaintCanvasBackground` walks up to the `html` box and, when the painted box is not it, passes the root's `ActualColor` through `PaintBackground` as `currentColor`. When `html` has its own background the box is `html` and nothing changes; a `body` gradient that is not propagated still uses the body's `color`.
