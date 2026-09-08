# A declared cell width now adds its own padding and border to the column

`box-sizing` defaults to `content-box`, so a table cell's declared `width` is its **content** width
and its padding and border sit outside it. The engine stored that bare content width as the column's
width — but a column width is an OUTER width everywhere else, since the same array is filled from
`cell.GetMinimumWidth()`, which already includes padding and border.

Every explicitly sized column therefore came out narrower than a browser's by exactly its padding
plus border, and the difference was handed to whichever column was `auto`. On a table of seven
px-width columns with `padding: 3px 5px`, each was about 8pt narrow and the auto column absorbed
roughly 58pt it should not have had.

Such a column is now as wide as the browser draws it. Measured against Chrome 152 on a cell
declaring `width: 100px; padding: 3px 5px; border: 1px solid`:

| | first column |
| --- | --- |
| Chrome 152 | 84.75pt |
| before | the same as an unpadded cell — 75pt |
| after | 75pt of content plus its own padding and border |

A cell declaring `box-sizing: border-box` is **unaffected** — its declared width already covers
padding and border, and Chrome puts that shape at the unpadded width too.

See [Tables](../../docs/html-css-support.md#tables) in `docs/html-css-support.md`.
