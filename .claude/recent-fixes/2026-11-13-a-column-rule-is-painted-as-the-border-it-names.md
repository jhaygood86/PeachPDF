# A column rule is painted as the border segment its style names, in layout units

Found by the WPT runner (css-multicol `multicol-rule-*`, 4 of 32 passing). `FragmentPainter.PaintColumnRules` stroked every rule with one pen:

- **`double`, `groove`, `ridge`, `inset`, `outset` painted solid** - the dash switch folded every style it did not name into `Solid`.
- **The pen width was in layout units, a pen's is in points**, so with a layout unit of one CSS pixel (`PixelsPerPoint` 96/72) a 10px rule was 13.33px wide. Another member of the `PointsPerPx`-ignores-the-layout-unit class (see [layout in css pixels](2026-11-12-a-layout-in-css-pixels-needs-every-pixel-constant-scaled.md)).
- **The ends and edges were not pixel-snapped**, so a rule ended on a fractional row beside a background that ends on a whole one.
- Dashes were stroked with a pen of their own, so their phase differed from the same border on a box.

## Fix
Every rule is now `BordersDrawHandler.DrawCollapsedSegment` over the rule's rectangle (the handler a collapsed table border uses: `StyledStrokeFitting` for dashed/dotted, two bands for double, two faces for groove/ridge/inset/outset), with the rectangle snapped by `DecorationPixelSnapping.Snap` under `SnapBoxDecorationsToCssPixels`. Both the vertical and the block-axis (vertical writing mode) rules go through it. A solid rule is now a filled polygon, not a `DrawLine`, so tests that asserted `DrawLine` for a solid rule assert `DrawPolygon`.

## Evidence
css-multicol 104 -> 120 (+16 rule tests, no losses in any module); `ColumnRulePaintTests` (new, bitmap-level) and the engine, PeachPDF and PeachBrackets suites green.
A full WPT run once exited 132 after css-flexbox and then completed on a rerun with identical results; not reproduced, not understood.

## Not done
The remaining `multicol-rule-*` failures (nested balancing, `-color-inherit`, `-stacking`, `-fraction`, `-large`, `-px`, `-shorthand-2`) were not looked at.
