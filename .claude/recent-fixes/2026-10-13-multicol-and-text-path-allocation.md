# Multi-column documents allocated 6.2 GB per render; most of it was avoidable

multicol-l (one `column-count: 2` container holding the whole 1,272-page document) allocated 6.2 GB per render against 4.0 GB for
the same text in one column, and spent ~20% of its time in GC pauses (live heap ~1.2 GB, so every gen2 is expensive). The
allocation profile named the columns' own costs plus several per-word costs shared with every document:

- **`FragmentEmitter.IsOverflowNoColumnClaims`** was reached through `claims |= ... IsOverflowNoColumnClaims(...)`, which runs the
  right-hand side for every word of every captured box even when the word is already claimed. Its row scan does
  `all.FindAll(...)` over `CapturedInstance` records (large structs: a region, two sets, two contexts, two boxes), copying the list
  per holder: ~1 GB of arrays and ~0.4 GB of closures per two renders. It is a pure query now asked only of unclaimed words.
- **`CssLayoutEngineColumns.BuildSegments`** copied the container's whole child list into a new "run" every fragmentainer pass:
  quadratic for a container holding the whole document. With no `column-span: all` child the one run is the list itself.
- **`DomUtils.GetLastLeftIntersectingFloatBox`** built a `CssFloatCoordinates` (a class) per word per line before
  `GetFirstIntersectingFloatBox` returned null because the document has no float at all; it returns early (still counting the one
  lookup the loop would have made).
- **`PdfGraphicsRenderer.DrawString`** turned the glyph ids into the hex literal through a `StringBuilder`, a string, two byte
  arrays and a Latin-1 decode per run (~10% of allocation); `PdfEncoders.FormatGlyphIdsAsHexLiteral` writes it once and a test
  pins it to the old chain at every length around the 24-unit line-break boundary.
- **`CssBox.NeedsPerCodepointFont`** split and trimmed the `font-family` list for every word; `HtmlContainerInt.FamilyNamesOf`
  does it once per distinct list.
- **`CssLayoutEngine.UpdateTrailingGraphemeContext`** concatenated, allocated a boundary array and substringed per word; plain
  ASCII after an ASCII context is the last character (CR LF excluded), pinned by a differential test against the general path.

**Measured** (warm, in-process): multicol-l 6.18 -> 3.93 GB allocated per render, prose-m 393 -> 327 MB. GC pause per render did
not move (3.6 s) because it follows the live heap, not the allocation volume.

**Left, and why.** 1.5 million `CssRectWord`s are 400 MB of the live heap (264 bytes each: the hyphenation, overflow-wrap,
pre-split and per-script fields are null for nearly every word of a Latin document). The same side-object treatment `CssBox` got
applies, but the fields written during ordinary line breaking have to be identified by counting non-default *writes* first (see
[the CssBox note](2026-10-12-cssbox-rare-state.md)); that is the next change here.

**Evidence.** Release suite green; PDF words and drawings identical to main on 19 benchmark documents (multicol-s/m/l among them),
199 fuzz documents and all 220 showcase PDFs; zero-warning rebuild.
