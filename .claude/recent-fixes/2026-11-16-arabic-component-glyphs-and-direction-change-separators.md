# Arabic component glyphs get a ToUnicode destination; a separator at a direction change stays beside its predecessor

Issue #2418 (Noto Sans Arabic, joined letter forms, mixed numbers and Arabic).

## ToUnicode destinations were empty

A font such as Noto Sans Arabic draws one letter as a body glyph plus separate dot glyphs. The shaper gives
the extra glyphs an empty cluster (`ClusterLength == 0`), and `CharacterMapInfo.AddShapedText` mapped each
glyph to its cluster's text, so those glyphs got `""` and the CMap carried `<013D><013D><>`. That is not a
valid destination (ISO 32000-2, 9.10.3) and PdfPig throws reading the page.

Leaving such a glyph out of the CMap is **worse**: MuPDF and PDFium read an unmapped code as the character
with that value, so glyph 45 extracts as `-`, 44 as `,`, 46 as `.` (measured with the bundled Arabic subset).
A zero-length cluster glyph therefore maps to `CharacterMapInfo.NoText` (U+200B), unless it already maps to
real text; a later real mapping replaces it. `PdfToUnicodeMap` also writes U+200B for any empty destination,
whatever put it there.

Not done: mapping the dots to the letter they decorate. That extracts every letter once per glyph. The body
glyph is shared between letters that differ only in dots (BEH/TEH share one), so one destination per glyph
cannot be exact for those; per-occurrence `/ActualText` stays the authority.

## The separator after a number was drawn at the other end of the run

`PrecedingWordEdge` only anchored a word separator to its predecessor when both words ran in the same
direction. Across a direction change it returned null and the separator was drawn against the word it
precedes, which for `42 <arabic run>` is the far end of the run. The text extracted as `42مقر` and the page
had a space after the run instead of after the number.

The layout reserves the separator's advance beside the predecessor, on the side the paragraph runs toward
(UAX #9 N1/N2 give the neutral the paragraph's direction when the two sides disagree). It is now anchored
there, and `SeparatorAnchor` carries the separator's own direction, because the word's level no longer says
which side of the edge the glyph goes on. The paragraph direction is read from the owner box's containing
block, not from the lowest level on the line: in `dir=rtl`, `42` is level 2 and an isolated run level 3, so the
lowest level is even.

## Evidence

`WordSeparatorGlyphTests` (both paragraph directions), `PdfToUnicodeMapTests` (Arabic subset font: no
`<>` destination, component glyphs map to U+200B). PeachBrackets.Engine.Tests, PeachPDF.Tests and
PeachDrawing.PDF.Tests pass on net10.0, except `PdfOutputBaselineTests`, which drifts the same way on
untouched code in this environment (zlib).
