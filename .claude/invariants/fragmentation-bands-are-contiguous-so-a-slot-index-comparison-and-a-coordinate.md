# Bands are contiguous, so a slot-index comparison and a coordinate comparison are the *same* statement

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

**Bands are contiguous, so a slot-index comparison and a coordinate comparison are the *same* statement** — one band's bottom is the next one's top, by construction on the uniform grid and by how `PageGeometryTable` is built. That is what let #400 (b) rewrite the page arm exactly rather than approximately. Writing them the same way is also what exposed that they had disagreed at exactly one point: `>=` on a page and `>` in a column, for a bottom edge landing one epsilon past the band bottom.
