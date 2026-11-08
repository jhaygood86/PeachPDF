# Collapsed borders of a `direction: rtl` table are resolved as in `ltr`

The cells of a table in a `direction: rtl` context are placed from the right (`CssLayoutEngineTable.MirrorColumnAxis`, at the one place a cell's `Location` is set), but the collapsed-border model is built with `DirectionMode.Ltr` (`_inlineStartBorder`/`_inlineEndBorder`) and `GetGridLineX` reads a grid line as the left edge of column *c*. For an `rtl` table with `border-collapse: collapse` the left and right outer borders, and the winner between a left and a right neighbour with different borders, can therefore be taken from the wrong side. Separate-borders tables, and collapsed ones whose borders are the same on every side, are unaffected.

To close it: pass the table's real direction into the border model, and swap the two branches of `GetGridLineX` (and the matching lookup used for the segments) when the table is `rtl`.
