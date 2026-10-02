# COLR color fonts over CFF outlines, more composite modes, exact gradient geometry

Before: a `COLR`/`CPAL` font with CFF outlines rendered as plain/blank text; `PaintComposite` modes other than the blend modes
were painted as source-over; a COLR radial gradient's inner radius and `repeat`/`reflect` extend, and a linear gradient's `p2`,
were approximated. Now: CFF color fonts paint in color, `CLEAR`/`SRC`/`DEST`/`DEST_OVER`/`SRC_IN`/`DEST_IN`/`SRC_ATOP`/`DEST_ATOP`
composites are honored, and those gradient cases are exact for concentric circles / any `p2`.
