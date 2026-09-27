# Hinted variable TrueType fonts use their `cvar` control values

**Before:** a variable TrueType font hinted at a location (the raster backend, or a caller asking the engine for a grid-fitted outline) ran its
instructions with the control values of the default design, whatever the weight or width, so a stem or height the instructions take from the
control values was fitted to the default design's number.

**Now:** the control values move with the location (the font's `cvar` table), so hinted stems and heights follow it. Only a variable font that has a
`cvar` table and hinting instructions that read control values is affected, and only in hinted output (the raster backend's text and grid-fitted
outlines); vector PDF text, which is never hinted, is unchanged.
