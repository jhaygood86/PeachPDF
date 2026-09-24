# A footnote's note area stays with its call when `target-counter(_, page)` changes the layout

**Before:** in a document using both `float: footnote` and `target-counter(_, page)` (or `leader()`), resolving the
page numbers could change line-breaking enough to move a footnote call onto another page. The page's note area
was then computed for the earlier layout: a page could show a footnote call with no note at its foot, or a note
area could sit on the wrong page.

**Now:** footnotes and page floats are resolved again after each `target-counter` reflow, so the note area on a
page always belongs to the calls on it.
