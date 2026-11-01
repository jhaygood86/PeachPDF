# inline-table with non-table children is no longer empty (#1794)

**Symptom:** `A[<span style="display:inline-table">TXT</span>]B` rendered `A[]B`; text, spans, blocks and `::before` content
placed directly in an `inline-table` were not laid out or painted. A `display: table` (or an explicit `table-row` child) worked.

**Cause:** `DomParser.CorrectAnonymousTablesGenerateMissingChildWrappers` rule 2.1 (CSS 2.1 section 17.2.1) tested the parent
for `Keywords.Table` only, although the rule text says "'table' or 'inline-table'". The children were never wrapped in an
anonymous row/cell, so the table engine had nothing to lay out. Rules 3.2 and the rest of the pass already named both.

**Fix:** one condition, `is Keywords.Table or Keywords.InlineTable`. Rules 2.2/2.3 then apply to the generated row as for a table.

**Evidence:** `InlineTableAnonymousBoxesTests` (4 of 6 fail without the change). Rendered the issue's variants through the CLI and
extracted the text with PyMuPDF: `A[TXT]B`, `A[SPAN]B`, `A[DIV]B`, `A[GEN]B` (generated content only) and `A[ROW]B`, matching
the Chrome column of the issue. Engine 16951/16951 and `PeachPDF.Tests` green.

**Not done:** no baseline-alignment check against Chrome for the now-visible inline-table (the table sits on the line between
the surrounding text in the tests, but its exact baseline offset was not compared pixel-for-pixel).
