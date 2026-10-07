# Opt-in snapping of border widths to whole CSS pixels

**Before:** a `0.5px` border was 0.5px wide and a `1.5px` border 1.5px wide, always.

**Now:** `PdfGenerateConfig.SnapBorderWidthsToCssPixels` (CLI `--snap-border-widths-to-css-pixels`)
opts in to CSS Values 4's "snap a length as a border width", on the CSS pixel: 0 < w < 1px becomes
1px, w >= 1px rounds down to a whole pixel. It changes used widths and so box sizes, which is why it
is opt-in and separate from `SnapBoxDecorationsToCssPixels`. Default behaviour is unchanged; the
`thin`/`medium`/`thick` keywords are never snapped.
