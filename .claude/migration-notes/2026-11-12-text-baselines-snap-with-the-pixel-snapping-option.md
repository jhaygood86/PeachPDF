# With `SnapBoxDecorationsToCssPixels`, text baselines snap to whole CSS pixels; border widths snap on the right grid when layout runs in CSS pixels

Before: the option snapped boxes and borders only; a line of text stayed on its exact fractional baseline, so identical text could anti-alias
differently depending on the fractional position of the layout above it. A host whose render context lays out in CSS pixels
(`PixelsPerPoint` of 96/72) and set `SnapBorderWidthsToCssPixels` got borders one rounding step too thin (a `5px` border drawn 4.5 wide).
Now: with the option on and no canvas transform, each line of text is moved by at most half a CSS pixel so its baseline is on a whole pixel,
and border widths snap on the context's own CSS pixel. Output with the option on can move text by up to half a pixel vertically.
`RasterRenderContext` also gains `PixelsPerPoint` (default 1, no change for existing callers).
