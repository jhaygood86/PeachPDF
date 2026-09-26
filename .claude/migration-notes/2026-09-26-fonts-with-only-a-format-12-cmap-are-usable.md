# Fonts whose only `cmap` subtable is format 12 are usable

Before: a font (`@font-face`, `AddFontFromStream`, or a system font) whose `cmap` table had a format-12 subtable but no format-4 one was refused as a
whole ("no usable platform or encoding ID"), so text set in it fell through to the next family. Now it loads, and the format-12 subtable maps the Basic
Multilingual Plane as well as the astral planes. A font that has a format-4 subtable behaves as before (in the rare case that it has several format-12 subtables, a Unicode one is now preferred to an earlier one of another platform).

Nothing else changes for a document author; a font with neither subtable is still refused.
