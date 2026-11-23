# Arabic: per-occurrence /ActualText for dotted letters; the space before a trailing number

Issue #2451 (follow-up to #2418, #2420).

## Sibling letters shared one ToUnicode destination

Noto Sans Arabic draws BEH/TEH/THEH/NOON/YEH... as one dotless body glyph plus dot glyphs the shaper emits
with an empty cluster. ToUnicode has one destination per code, so the body glyph extracted as whichever
letter was drawn last. Two layers now:

- **A code of its own per letter (what PdfPig reads).** PdfPig's `Letters`/`Text` ignore `/ActualText`
  (verified: 16 `/ActualText` spans present, BEH/YEH paragraphs still read as TEH), so a ToUnicode entry has to be
  right. `CharacterMapInfo.AddShapedText` keeps the first text a glyph is drawn as on the glyph's own code and, on
  a conflicting later text, hands out a variant CID (from 0xFFFF down, clear of any glyph id) and returns the
  per-glyph codes, which `DrawString`/`DrawPositionedGlyphs` write instead of the glyph ids. The descendant font
  gets a `/CIDToGIDMap` stream (`PdfCidFont.WriteCidToGidMap`: identity for the glyphs of the subset, each variant
  naming its glyph), `/W` and ToUnicode list the variants, and PDF/A-1's `/CIDSet` includes them. The space rules
  are unchanged (a space is never replaced and always replaces), and a glyph that was only ever a zero-cluster dot
  (U+200B) is replaced in place, not varied.
- **Not for compact CFF subsets.** Their CIDs are the charset's and cannot be remapped, so such a font keeps the
  old last-wins destination (decided by a trial export, once per font, when the first conflict appears).
- **`/ActualText` stays** for any run holding a glyph with no cluster of its own, for readers that honor it and for
  the CFF case. The dots still map to U+200B.

A separator that follows an `/ActualText` run is not written with a bare `Tj` at the pen (the `EMC` changes the
content length `TryShowAtPen` checks), so it gets its own `Td`.

## The space between Arabic and a number after it was placed at the far end of the run

`ApplyBidiReordering` kept each run's trailing gap after the run in visual order. For `<arabic> 42` the number
(level 2) splits the level-1 run, and the gap between the Arabic word and the number ended up between the
Arabic and the full stop. `PlaceGapsBetweenRuns` now leaves the gap between two reading-order neighbours when
reordering puts them side by side (the number visually left of the word it follows), and otherwise keeps the
old placement. The painter (`PrecedingWordEdge`) anchors the separator on the side the word really sits on when
it is right next to its predecessor, falling back to the paragraph's direction (the `42 <arabic>` rule from
#2420) otherwise.

## Evidence

`WordSeparatorGlyphTests` (both paragraph directions), `WordSeparatorPdfOutputTests` (`/ActualText` and
`/CIDToGIDMap` with the Arabic subset), `PdfToUnicodeMapTests`. PdfPig reads all letters of a 27-paragraph
per-letter document correctly, MuPDF and PDFium draw the variant codes. PeachBrackets.Engine.Tests, PeachPDF.Tests and PeachDrawing.PDF.Tests pass on net10.0.
