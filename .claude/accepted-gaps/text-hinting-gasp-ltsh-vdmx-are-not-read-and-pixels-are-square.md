# Text hinting: `gasp`, `LTSH` and `VDMX` are not read, and pixels are square

`hdmx` is used (as FreeType does), but `gasp` (which sizes want fitting), `LTSH` and `VDMX` are not read, so a caller that asks for fitting
gets it at every size. A size is one number, so FreeType's stretched-ratio (non-square pixel) paths in the interpreter are not ported.
Layout metrics are unhinted on purpose, which is why the metric tables do not matter to it. Tracked in
[#1433](https://github.com/jhaygood86/PeachPDF/issues/1433).
