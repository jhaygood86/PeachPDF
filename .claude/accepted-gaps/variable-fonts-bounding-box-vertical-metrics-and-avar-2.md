# Variable fonts: bounding box, vertical metrics, `avar` 2 and colour fonts stay at the default design

An instance from `Typeface.WithAxes` keeps the default design's font bounding box (`head`, which `MVAR` does not vary), vertical metrics
(`VVAR` and the vertical phantom points are not read), reads only the version 1 `avar` maps, reads `COLR` version 1 variable paints
(`PaintVar*`, `VarColorLine`, `VarAffine`) at the default instance, and ignores `cvar` (outlines are not grid-fitted). Tracked in
[#1408](https://github.com/jhaygood86/PeachPDF/issues/1408).
