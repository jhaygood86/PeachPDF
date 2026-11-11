# A multi-column container sized to its content is as wide as all its columns

A multi-column container whose width comes from its content (a float, a flex item, a table cell, an `auto` width `position: absolute` box)
used to be as wide as one column of its content. It is now `column-count` columns of the content's width plus the gaps (and never narrower than
`column-width`), as in a browser: a `float: left; columns: 3` box of text is three times as wide as before, and a `column-width`-only one is at
least its `column-width`. A `column-span: all` child no longer counts as part of the column but keeps the container at least as wide as itself.
Documents that gave the container an explicit `width` are unchanged.
