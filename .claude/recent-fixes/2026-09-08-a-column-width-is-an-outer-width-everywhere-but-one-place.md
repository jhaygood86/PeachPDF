# A column width is an outer width everywhere but the one place that set it from CSS

`_columnWidths` holds OUTER widths: `GetColumnMinWidths` fills it from `cell.GetMinimumWidth()`,
which includes padding and border. The clause that reads a cell's declared `width` stored the bare
parsed length instead — a CONTENT width under `box-sizing: content-box`. So the array mixed two
meanings, and every explicitly sized column came out narrow by exactly its padding plus border,
with the shortfall handed to whichever column was `auto`.

## What running it turned up

- **`box-sizing` had to be read, not assumed.** The obvious fix — always add padding and border —
  is wrong for `box-sizing: border-box`, where the declared width already covers them. Chrome puts
  that shape at the *unpadded* width, so always-adding would have traded one wrong number for
  another. `CssBox.ActualBoxSizeIncludedWidth` already makes exactly this distinction (padding +
  border for content-box, zero for border-box), so the new helper is only its writing-mode switch,
  mirroring `CellInlineSize`'s.
- **Chrome measurements, on the three shapes that separate the cases** — `width: 100px` with
  `padding: 3px 5px; border: 1px`, the same without padding, and the same under `border-box`:
  84.75pt / 77.25pt / 76.5pt. Padded is ~7.5pt wider than bare; border-box is not wider at all.
  Asserted as relationships rather than absolute points, since the two engines disagree about font
  metrics and border-collapse rounding.

## Evidence

`TableDeclaredCellWidthTests`, three fixtures: padded wider than bare (fails against `main`),
border-box equal to bare, and an unpadded 100px cell still exactly 75pt so the change is a no-op
where nothing is owed. Full suite green on net8.0 (10,255), 0 new build warnings.
