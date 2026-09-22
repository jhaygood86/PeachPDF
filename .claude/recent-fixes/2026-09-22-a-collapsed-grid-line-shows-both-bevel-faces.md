# A collapsed grid line shows both bevel faces, not one side's (#1237)

`BorderBevelColors.ForSegment` mapped every collapsed segment to `Border.Top`/`Border.Left` — both of
which darken under `inset` — so a `border-collapse: collapse` table painted its whole frame in one
face and neither `inset` nor `outset` was distinguishable from `solid`. `MarginBoxRenderer.PaintBorder`
paints a page box's and a page-margin box's border through the same primitive, so it had it too.

**Only `inset`/`outset` changed.** `groove`/`ridge` already took the two-band path, and there the old
top/left convention picked the same two faces in the same order the new rule does
(`ForSide(Top, x) == ForSide(Left, x) == Shade(color, x)`, and the leading band was the outer one) —
byte-identical before and after, on a collapsed line and on a margin box alike. Their rows in the new
four-keyword theory are regression pins that would also pass on the reverted code; that is deliberate
(they are what says the two pairs now coincide), not an oversight.

## The load-bearing idea: the issue's suggested fix was wrong, and measuring said so

The gap note and the issue both proposed threading the *originating* `Border` side through from
`CollapsedBorderModel`, and flagged "which cell a shared grid line belongs to" as the open question
needing a measurement pass. That pass says the question does not arise: **Chrome attributes a grid
line to no side at all.** Every collapsed line — outer and interior, cell-declared and
table-declared — is painted as two half-bands, the half at the smaller coordinate taking the
bottom/right face and the half at the larger coordinate the top/left face. Which way the line *runs*
plays no part either; `isHorizontal` is geometry only, and the bevel question no longer consults it.

So `ForSegment` lost its direction argument entirely and now returns *both* faces at once —
`(RColor Leading, RColor Trailing)`, rather than taking a `bool` selector for which half is being
asked about, since that selector and `inset` would be interchangeable at every call site and a
swapped pair would still compile. `DrawCollapsedSegment` grew a `Border? side` instead: non-null for
`MarginBoxRenderer`'s four real edges (which take one face per side, like any box), null for a grid
line (which takes both).

## What was found by running it rather than by reading it

Probed against Chrome 153 headless (`--screenshot`, `--force-device-scale-factor=1`), pixel-mapped
with PIL:

- **A collapsed `inset` is pixel-identical to a `ridge`, and an `outset` to a `groove`.** Not an
  inference from the half-band rule — the four were rendered side by side and compared. Anyone
  tempted to "fix" the fact that two keyword pairs now look alike on a collapsed table should render
  them in a browser first.
- **The rule is indifferent to ownership.** A grid line whose entire width came from one cell's
  declaration (the neighbour declaring no border at all) still splits its two faces down the middle.
  This is the specific thing that would have been got wrong by threading the winning candidate's
  side through, and the reason that approach was abandoned.
- **Blink does not split inset/outset for an ordinary box** (`CalculateBorderStyleColor` returns one
  colour per side), so the split is not a property of the style — it is a property of the line having
  two sides. The separate-borders model is untouched and still matches Chrome exactly.
- **At odd widths Chrome gives the leading band the extra pixel** (1px → entirely the lit face,
  3px → 2 lit + 1 dark, 5px → 3 + 2). Not reproduced, deliberately: PeachPDF splits at the exact half
  in PDF points, as `groove`/`ridge` segments already did, and a vector split has no pixel to hand to
  either side. The visible residue is that a 1px collapsed bevel rasterizes as a ~50/50 blend of the
  two faces where Chrome shows one clean face — below the noise floor at that width, and deliberately
  not given a docs line or a gap file, so this bullet is the whole record of the decision and is
  meant to die with this note.
- **`groove`/`ridge` on a page-margin box were never broken**, though they look like they should have
  been. On a bottom or right edge the face flip (`ForSide(Bottom, x) == ForSide(Top, !x)`) and the
  outer/inner band-order flip cancel exactly, so the side-blind code emitted the right two colours in
  the right order. `AGrooveMarginBoxBorder_PutsItsInsetFaceOnTheOuterHalfOfEverySide` pins that,
  because a future change teaching this path about sides could flip one without the other and still
  look right on a top edge.

## Deliberately not done

- **A page/margin box's border corners still overlap rather than mitre**, and giving its four edges
  different faces for the first time is what made that conspicuous: with `border: 20px inset` the
  top-right corner comes out entirely lit and the bottom-left entirely dark, because `PaintBorder`
  paints four full-span rects in the order top, bottom, left, right. Pre-existing and more general
  than the bevel — `border-top: solid red; border-right: solid blue` already paints a fully blue
  corner, and `AllFourEdges_PaintTheirOwnIndependentlyResolvedWidthAndColor` *pins* the full-span
  rects — so closing it is a rewrite of that test, not a passing-test change. Issue #1258, with
  [../accepted-gaps/margin-box-border-corners-overlap-instead-of-mitring.md](../accepted-gaps/margin-box-border-corners-overlap-instead-of-mitring.md).
- **A collapsed table's outermost grid lines still sit half outside the table's border box**, so at
  the page/container edge the outer half is clipped and the four outer corner squares go unpainted.
  Chrome sizes the table to contain the whole outer line and fills those corners: measured over one
  table, 7800/7800 px per face in Chrome against 7600/7600 here, the whole 400px deficit being the
  four 10×10 corners. Pre-existing, unrelated to shading (this change touches no geometry), and its
  own issue — see [../accepted-gaps/collapsed-table-outer-grid-line-overhang.md](../accepted-gaps/collapsed-table-outer-grid-line-overhang.md).

## Evidence

- Full suite green (13,119 passed, 9 skipped / net8.0), whole solution rebuilds with **0 warnings**,
  **100% diff coverage** (`diff-cover`: 31 measurable changed lines, 0 missing).
- New tests: a four-keyword theory over a collapsed table asserting both faces on both axes with the
  band thickness pinned at half the line, an interior-grid-line test proving the shared line between
  two cells splits (and that its two bands abut rather than being two lines), and three direct
  `MarginBoxRenderer.PaintBorder` tests through `TestRecordingGraphics` asserting an exact `RColor`
  per edge — the content-stream `rg` operator rounds to three decimals and would not tell two near
  faces apart.
- `MarginBoxRendererBorderTests.ABevelledEdgeWithNoDeclaredColor_...` gained the lit-face assertion
  its own comment had been waiting on (`0.933` on a bottom edge, against `0.604` on the top).
- `border_style` showcase gained a section pairing each of the four bevels collapsed-above-separate,
  rendered through PDFium and compared against Chrome printing the same `border_style.html`.
