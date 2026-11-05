# `<center>` and `align` on `td`/`div`/`p` are a plain `text-align`, not `-webkit-center`

A browser maps these to the legacy `-webkit-center`/`-webkit-left`/`-webkit-right` alignment, which a table does not inherit and which centers a block-level child with a definite width (a table, a `div` with a `width`) as a box. Here they are an ordinary `text-align`, so `<td align=center><table>` centers the table's cell text and does not center the table box (tracking issue #2186).

What is covered: a table with its own `align` attribute resets its `text-align` to `start` (`DomParser.TranslateTableAlign`), which handles `<td align=center><table align=center|left|right>`. It resets a CSS `text-align: center` inherited from the parent too, which a browser would pass into the table; nothing records where an inherited value came from, and that is what a proper fix needs.
