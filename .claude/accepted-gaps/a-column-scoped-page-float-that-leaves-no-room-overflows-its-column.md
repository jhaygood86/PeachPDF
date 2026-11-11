# A column-scoped page float that leaves its column no room overflows it

A limit on `float-reference: column` page floats (`float: top`/`bottom`/`top-bottom`/`snap`):

- **A float, or a float and a note area together, at least as tall as the column is declined rather than
  reserved.** `CssLayoutEngineColumns.FillColumns` drops the reservation when it would leave the column no
  room, because a column with nothing placeable in it defers page after page. The float is still placed at
  its edge, so it overlaps the column's content. The same is true of a footnote area taller than its column,
  and of a float written among inline content that does not fit its column
  ([a-float-that-does-not-fit-in-its-column-overflows-it.md](a-float-that-does-not-fit-in-its-column-overflows-it.md),
  [#1341](https://github.com/jhaygood86/PeachPDF/issues/1341)).
