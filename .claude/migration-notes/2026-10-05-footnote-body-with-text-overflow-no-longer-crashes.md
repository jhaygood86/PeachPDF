# A `float: footnote` body with `text-overflow: ellipsis` no longer fails the render

**Before:** a footnote whose body declared `overflow: hidden; text-overflow: ellipsis` made the whole render
throw a `NullReferenceException`.

**Now:** the render succeeds and the footnote text is painted in full; the ellipsis is not applied inside a
footnote body (`text-overflow` has no effect there), and a `nowrap` note can run past the body's width.
