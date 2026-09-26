# Text hinting: variable-font hinting is not bit-exact with FreeType, and `cvar` is not applied

A variable-font instance is hinted after this package's own double-precision `gvar` deltas move its points; FreeType does that in 16.16
fixed point (`ttgxvar.c`), so results can differ by a fraction of a pixel. `cvar` (per-location control values) is not applied. The
golden tests against FreeType cover static fonts only. Tracked in [#1432](https://github.com/jhaygood86/PeachPDF/issues/1432).
