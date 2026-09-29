# Repeating backgrounds and border-image still draw one image per tile

`background-repeat` (`BackgroundImageDrawHandler.DrawRepeat*`) and `border-image-repeat` (`BorderImageDrawHandler`) tile with a
loop of `DrawImage` calls, not with a `TileBrush`, although the brush now exists and SVG `<pattern>` fills use it.

Why it was left: the layout suites assert the per-tile destination rectangles a repeating background produces (and the Acid2
regression test counts them), so a switch changes the contract those tests pin, and a background tile's seam behaviour in
PDF viewers differs between a run of adjacent image draws and a tiling pattern (viewers snap pattern cells to device pixels
differently). Switching needs both: tests rewritten to assert the brush's cell, size and transform, and the two-renderer
rasterization of the Acid2 checkerboard and the border-image showcases.

The brush is ready for it: `new TileBrush(image, tileWidth, tileHeight, Matrix3x2.CreateTranslation(x, y), sampling)` filled
over the visible rectangle replaces `DrawRepeat` exactly, and `image-rendering` already resolves to the `sampling`.
