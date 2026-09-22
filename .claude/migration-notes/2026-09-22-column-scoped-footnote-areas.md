# Footnotes can now be scoped to their column, and multicol respects the page's note area

**Before:** a `float: footnote` note always went to the bottom of the page, whatever container its
reference sat in. And a multi-column container on a page that carried a footnote was never told about
the reserved strip at that page's bottom, so its column content flowed straight over the note area.

**Now:**

- **`float-reference: column`** ([css-page-floats](https://drafts.csswg.org/css-page-floats/)) routes
  a note to the foot of the column its reference landed in. Each column gets its own note area - its
  own divider, its own width, its own reserved strip - and a page can carry both a column-scoped and a
  page-scoped area at once, since the property is set per footnote. The same `@footnote` rule styles
  both, with percentages resolving against the column the area actually occupies.
  `float-reference`'s other values (`page`, and the initial `inline`, and `region`) all keep the
  page-level behaviour for a footnote, so **no existing document changes**.
- **Multicol content now stops above the page's footnote area.** This is a visible pagination change
  for any document that combines a multi-column container with a footnote on the same page: column
  content that previously overlapped the note area now ends above it, which can push later content
  onto a further page. Measured on the fixture that caught it, column content ran to 369.6 where the
  reserved strip began at 367.

**Numbering is unaffected by placement.** `float-reference` decides where a note goes and says nothing
about counters, so two notes in two columns of one page are numbered 1 and 2, not 1 and 1.

**`float: inline-footnote` is still not supported, and the documentation no longer calls that a gap.**
It is a PrinceXML extension that appears in no revision of css-gcpm-3, and the behaviour it selects in
Prince - the marker placed inside the footnote body rather than hanging in the margin - is already
PeachPDF's only behaviour.
