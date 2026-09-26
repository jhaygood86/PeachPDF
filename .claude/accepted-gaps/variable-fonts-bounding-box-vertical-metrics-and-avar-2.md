# Variable fonts: colour paints and `cvar` stay at the default design

An instance from `Typeface.WithAxes` reads `COLR` version 1 variable paints (`PaintVar*`, `VarColorLine`, `VarAffine`) at the default instance,
and ignores `cvar` (outlines are not grid-fitted). Tracked in [#1408](https://github.com/jhaygood86/PeachPDF/issues/1408). (The font bounding
box, vertical advances and origins, and `avar` version 2 are read at the location.)
