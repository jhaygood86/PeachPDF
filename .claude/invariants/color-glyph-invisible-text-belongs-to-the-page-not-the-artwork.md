# A color glyph's invisible text belongs to the page, not to its shared artwork

A COLR/CPAL glyph reaches the PDF as two separate things, and only one of them is shareable:

- the **visible ink**, which is identical wherever the glyph lands, and so lives in a Form XObject
  cached per document (`ColorGlyphFormCache`) and invoked with `Do` at each occurrence;
- the **rendering-mode-3 text object** that supplies selection geometry, whose `/ActualText` describes
  *that occurrence's own* source sequence.

The second one is not shareable, and the reason is not merely bookkeeping: the same glyph id can
represent different source text at different occurrences — ❤ with and without VS16 is the canonical
case, and `ColorGlyphPainter.BuildActualTextByGlyph` exists precisely to work that out per run. Move
the text object into the form and every occurrence copies the first one's source text; drop it and
the glyph stops being selectable and searchable at all.

So `ColorGlyphPainter.Paint` paints all the artwork first (through the form cache), and only then
opens **one** `BT`/`ET` on the page carrying one `/ActualText` span per source-bearing glyph. A future
change that widens what the form holds must leave that text object on the page.

The symptom if this is broken is quiet: the document renders pixel-identically and only text
extraction is wrong — the wrong emoji sequence copied out, or nothing selectable where ink clearly is.
`ColorGlyphFormReuseTests.InvisibleSelectableText_StaysOnThePage_OncePerOccurrence` is the guard: it
asserts one `Tj` per occurrence on the page and **no** text object inside the form's own stream.

Related: the form is invoked by a bare `Do` with no `/Group`, so the ink composites exactly as the
inlined paths did — see
[../recent-fixes/2026-09-10-color-glyph-artwork-drawn-once-into-a-form-xobject.md](../recent-fixes/2026-09-10-color-glyph-artwork-drawn-once-into-a-form-xobject.md).
