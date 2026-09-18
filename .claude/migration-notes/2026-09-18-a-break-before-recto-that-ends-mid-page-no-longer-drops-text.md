# A `break-before: recto` chapter that runs past its first page no longer drops text

Previously, when a block after a `break-before: recto` (or `right`, `left`, `verso` - any directional value
that steps over a page) was taller than the page it landed on, and that first page ended in the middle of the
block, the text on that first page and on the pages after it could silently disappear from the PDF, and later
`recto` chapters could open on a left-hand page. It affected a book-shaped document - a cover followed by
chapters that each start `recto` and each run for several pages - and shipped in v0.9.18: a minimal document
with one `break-before: recto` chapter of about eight long paragraphs lost text there too. The
`paged_media_directional_breaks` showcase lost its whole first chapter (heading and paragraphs 1-5) and came
out at 8 pages.

Now the whole chapter is laid out and every chapter opens on a right-hand page, with the reserved blank
left-hand page before it. The showcase renders 9 pages, and its colophon lands on page 9.

Documents with no forced directional break, or whose directional-break blocks always fit on the page they land
on, are unaffected. There is nothing to change in a document to opt in; a document that had worked around the
loss (shortening a chapter, say, or moving the break to a wrapper) keeps rendering correctly.
