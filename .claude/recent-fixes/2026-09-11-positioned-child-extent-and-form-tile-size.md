# 2026-09-11 — The chart axis drew twice: a positioned child's extent, and a truncated tile

Charts.css's column chart showed a black bar and a grey bar at its foot, stacked. In a browser they are
one line: the black **primary axis** is `tr { border-block-end }`, the grey is the last **secondary axis**
grid line — a repeating `background-size: 100% calc(100% / 4)` tile on the *same* `tr`, whose fifth tile
lands in the bottom border strip because `background-clip` defaults to `border-box`. The border paints
over it, so only black shows. Two unrelated defects pulled them apart.

## 1. An absolutely-positioned child inflated its ancestor's decoration rect

Instrumenting that `tr` at paint time:

```
decoH=147.857   boundsH=131.357   marginBottom=16.500
```

The box lays out 131.357pt tall — correct; the flex stretch properly subtracts its own
`margin-block-end`. But paint received a decoration rect of 147.857pt: the **margin box**, 16.5pt taller.

`FragmentEmitter.ExtentOf`'s `BoundsEndAtItsContent` branch grows a box's painted extent to cover its
children. That exists for a real reason — content that overflows bounds pinned before it was known
(#569) — but it counted **out-of-flow** children too. Charts.css's axis labels are `position: absolute`
with `margin-block-start: auto` and a negative block-end margin *precisely so they hang below their row*,
and they dragged the row's own border down with them. The confirmation was to hide just the label
(`tbody tr th { display: none }`) in the otherwise identical document: `decoH` dropped to 131.357, an
exact match.

Fix: skip `Absolute`/`Fixed` children in that loop. CSS 2.1 §9.3.1 takes them out of the flow entirely
and §10.6.3 sizes a box from its in-flow content alone. The in-flow case is untouched and guarded by its
own test, since that is what the extension is for.

Visible consequence beyond the doubled line: the `Q1`–`Q4` labels sat *above* the axis, where a browser
puts them below it. Easy to read as intentional; it was the same 16.5pt.

## 2. A Form XObject tile's natural size was truncated to whole points

With (1) fixed the two lines were still ~2.6pt apart, and the grid step measured 32.0pt where
`background-size` had resolved 32.6518pt. The resolution was right — printing it showed
`tileH=32.6518, x4=130.6071`, exactly the padding box — so the error was downstream, in the repeat.

`CssImagePainter` renders a gradient tile into an `XForm` via `RGraphics.CreateTile` and then repeats it
at `Keywords.Auto`, i.e. the image's natural size. `ImageAdapter.Width`/`Height` returned
`XImage.PixelWidth`/`PixelHeight`, which for `XForm` are **`(int)` casts of its view box**. A form has no
pixels, so that truncation is meaningless in itself — and destructive when repeated, because the error is
not sub-point but accumulates once per tile: 32.6518 → 32, and by the fourth tile the grid line was 2.6pt
clear of the axis.

Fix: `ImageAdapter` reports `PointWidth`/`PointHeight` for an `XForm` and keeps `PixelWidth`/`PixelHeight`
for a raster `XImage`. The tiling call site already declares `intrinsicSizeInCssPixels: false` for tiles,
so it was asking for points and being handed truncated points.

**Worth knowing:** this affects every tiled Form XObject, not just backgrounds — SVG patterns, masks and
opacity groups take the same path. Nothing else regressed, but a tile whose size is not a whole number of
points was previously placed slightly wrong everywhere, and the symptom is always the same shape: a
seam or drift that grows across repetitions rather than a uniform offset.

## Evidence

- `dotnet test --framework net8.0`: 10759 passed, 9 skipped, 0 failed (plus 96 CLI, 119 generator).
- Diff coverage vs `main`: 117 executable changed lines, 0 uncovered.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Pixel-measured before and after at 5× on the real showcase: grid lines now step 163.26px (32.65pt)
  exactly, and the fifth lands at 1526.5px — inside the black border at 1526–1529, which is what makes
  them one line again. All 113 showcases regenerated and checked through PDFium and MuPDF.
- New tests: `PositionedChildDecorationExtentTests` (3) and `FormTileNaturalSizeTests` (2). Both were
  confirmed to fail with their own fix reverted.

**Debugging note:** the first measurement of this came from scanning rendered pixel columns, and it sent
me down one wrong path — the grid *appeared* to start 11pt below the row's top, which looked like a
background-origin bug. It was the inflated extent shifting everything. Printing the geometry the painter
actually receives (`decoRect` vs `Bounds` vs `ActualMarginBottom`) settled in one run what pixel
archaeology had muddied; prefer it.
