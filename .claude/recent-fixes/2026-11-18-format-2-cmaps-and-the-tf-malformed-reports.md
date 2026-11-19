# Format 2 cmaps: the "Tf (malformed)" failures

Four pdf.js files (bug1200096, issue14117, issue5501, issue5701; pdf.js corpus 773 -> 777) reported `Tf (malformed)`: not a malformed operator but an
`InvalidOperationException` from the font loader (`CMapTable.Read`: no usable cmap) caught by the interpreter's catch-all, which names only the
operator. The embedded TrueType fonts carry a format 2 (high-byte) cmap, on the Unicode and Windows platforms and sometimes a Macintosh format 0, and the loader
reads only format 4 and 12 there.

`SfntRepair.ReadUnusableCmap` (was `ReadMacOnlyCmap`, which gave up whenever *any* Unicode-platform subtable existed, usable or not) now looks for a
usable one (platform 0 or Windows Unicode/symbol, format 4 or 12) and, when there is none, carries the best of the one-byte tables (format 0, 6 or
the single-byte part of 2) over at U+F000 + code. The two-byte part of format 2 is not read: the fonts are simple (one-byte) fonts here.

**Trap:** an `unsupported` entry that says `(malformed)` can hide any exception type; the runner's `PEACHDRAWING_RUNNER_STACKS=1` shows the top
frames of a thrown failure but not one the interpreter swallowed, so a temporary line printing `e.GetType()` in the catch (not kept: it would put a
test hook in the library) is how this was found.

Left: `Tf (missing font)` on issue5954 (the page's own /Resources has no /Font, the Pages node's does: PDFium finds it, this importer follows the
spec and does not merge) and bug1669099 (`/ 12 Tf`, an empty font name).
