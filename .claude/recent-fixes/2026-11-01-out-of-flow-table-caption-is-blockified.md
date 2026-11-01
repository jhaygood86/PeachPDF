# An absolute/fixed `display: table-caption` is blockified (#225, #1238)

`DomParser.BlockifyPositionedBox` already turned every table-internal display except `table-caption`
into `block` for `position: absolute`/`fixed` (CSS 2.1 §9.7 table; CSS Display 3 §2.7). The caption arm
was the one missing, so a positioned caption kept routing through the table engine and kept the
table-type border-colour exemption (`BorderBevelColors`). Added the arm; no layout change for any
other display value.

Found by running, not reading: the crash half of #1238 (`position: fixed; display: table-row`, #1239)
and the cell/row arms were already fixed on `main`, so both issues were stale except for the caption.

Not done: `position: absolute; display: table` with no rows painting nothing (#1238 "also found") was
not reproduced here; file it separately if it still shows.

Evidence: `AbsolutePositioningIntegrationTests.OutOfFlow_TableInternalDisplay_BlockifiesToBlock` gains the
caption row; `BeveledBorderCurrentColorTests` pins that a positioned cell/row/caption bevels from the
fixed base, like Chrome 153.
