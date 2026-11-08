# `<center>` and `align` on `td`/`div`/`p` are a plain `text-align`, not `-webkit-center`

A browser maps these to the legacy `-webkit-center`/`-webkit-left`/`-webkit-right` alignment, which a table does not inherit. Here they are an ordinary `text-align`, so `<td align=center><table>` centers the table's cell text where a browser leaves it at the start edge (tracking issue #2186).

Covered: a block-level table that is a direct child of `<center>` or of an element with `align=center` is centered as a box (`DomParser.CenterTableInLegacyCenteredParent`, a presentational hint a table's own `align` or an author margin beats); and a table with its own `align` attribute resets its `text-align` to `start`, which covers `<td align=center><table align=...>`. The reset also applies to a CSS `text-align: center` inherited from the parent, which a browser would pass into the table; nothing records where an inherited value came from, and a proper fix needs a distinct legacy keyword.
