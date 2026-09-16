# Top- and bottom-aligned replaced elements size the line once

Acid2 exposed a red/yellow strip above its eyes after the shared-baseline work. The `#eyes-b` and
`#eyes-c` rows were 18pt high, but the resolved 18pt eye image ended 6.175pt lower. The line first
treated the image's entire margin box as baseline-aligned, adding it above the baseline, and then
`vertical-align: bottom` moved the same box to the line's bottom. The strut descent was therefore
counted in addition to the image height.

## The load-bearing idea

`CssLineBox` now keeps its baseline-aligned extent separate from the maximum heights requested by
top- and bottom-aligned replaced margin boxes. `GrowLineToItsExtent` derives the public combined
extent from those three values each time instead of permanently assigning an edge-aligned image to
one baseline side. This keeps the result independent of whether taller baseline content appears
before or after the image. The speculative pre-wrap path snapshots and restores all three values
together.

The same effective `vertical-align` resolver is used during open-line sizing and closed-line
placement, including `::first-line` overrides and the table-cell exception. Placement aligns the
replaced element's margin edge, not its border edge, so the geometry being positioned is the same
geometry that sized the line.

The review pass found all three follow-up cases. The initial WIP fixed Acid2 itself but was
source-order-dependent, ignored non-zero margins during final placement, and still double-counted an
image aligned through `::first-line`.

## Evidence

- `BaselineAlignmentLayoutIntegrationTests` covers isolated top/bottom images with non-zero margins,
  source-order independence beside taller baseline content, and a `::first-line` top override.
- `Acid2RegressionTests.Eyes_BottomAlignedObject_CoversTheWholeCheckerboardRow` asserts that the
  resolved object fragment and `#eyes-b` row have identical top and bottom edges.
- Full net8.0 suite: 12,059 passed, 0 failed, 9 platform skips.
- Full solution rebuild: 0 warnings, 0 errors.
- Diff coverage against the WIP commit's parent: 100%.
- The regenerated Acid2 showcase was rasterized through PDFium and MuPDF. Both place the eye row at
  the correct height without the red/yellow strip; their remaining stipple difference is the
  documented low-resolution checkerboard filtering gap.
