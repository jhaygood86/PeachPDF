# #1481, #1482 and #1483 no longer lose words; their repros are pinned

Three fuzz reductions (a `break-inside: avoid` float taller than the band in a table cell, a list item with a table beside a
tall float, a float that overhangs its wrapper) were filed on Sep 27, two days before the work that lets a tall float continue
onto later pages with its text (#1520). They had no tests. Re-checked on `main`: every authored word is in the fragment tree
exactly once in all three.

Two ways the check can be wrong, both met while confirming this:
- `PaintedWords.LayOutAndCollectVisibleAsync` keeps only words whose rectangle lies inside the page band within 0.5pt. A
  heading's first line at the top of a page has a glyph box about 0.7pt above its line box (11.7pt Arial in a 12pt line), so
  `w554_840`, `w382_458` and `w5535_204` were reported lost though they are placed at the top of the next page. For a
  loss-of-content check at a page break, count the words in the fragment tree instead (what these tests do).
- The #1483 reduction has one token glued by the reducer (`w5535_34w5535_76`), which reads as two lost words.

A PDF text extraction (PyMuPDF `get_text`) is no substitute either: it returns text a clip path hides, and the machine's own
Arial has different metrics from the font these reductions were found with. #1369 is not pinned for that reason: with the
pinned sans-serif its document fits one page, so it cannot exercise the break.

`PageBreakLostContentReproTests` pins #1481, #1482 and #1483.
