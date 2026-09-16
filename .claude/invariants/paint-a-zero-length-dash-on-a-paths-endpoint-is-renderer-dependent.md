# A zero-length dash landing on a path's endpoint is renderer-dependent

A dotted line is drawn as a zero-length dash under a round line cap: PDF paints a filled circle of
the pen's width at each one (PDF 32000-1 §8.4.3.3/§8.4.3.6). Both PDFium and MuPDF render this
identically, so it is the right way to express a dot — `StyledStrokeFitting` relies on it, and
`XPen.DashPattern` permits a zero element specifically for it.

What is **not** portable is a dash landing exactly on the path's end. Measured: with a path of length
160 and a dash array of `[0 32]`, the dot at offset 160 is painted by MuPDF and **dropped by PDFium**.
Extending the path by 0.05 was not enough for PDFium either.

So a fitted dot run must never end on its final dot. `StyledStrokeFitting.Apply` runs the path half a
period past the last dot's centre — anything strictly between 0 and one full period is safe, since
the next dot would need a whole period, and half is the most forgiving choice. Verified to produce the
same dot count in both engines.

The same reasoning applies to any future feature that positions marks with a dash array rather than
drawing them individually (`text-decoration-style: dotted`, SVG markers along a path): never depend on
a mark exactly at the path's end.
