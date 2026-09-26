# Variable fonts: cvar is not applied

An instance from `Typeface.WithAxes` ignores `cvar` (outlines are not grid-fitted, so nothing uses the control values). Tracked in
[#1408](https://github.com/jhaygood86/PeachPDF/issues/1408). (The font bounding box, vertical advances and origins, `avar` version 2 and the
`COLR` version 1 variable paints are read at the location.)
