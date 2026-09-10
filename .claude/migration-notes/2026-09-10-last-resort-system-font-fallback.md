# Uncovered characters now fall back to any other registered font

_Landed 2026-09-10._

Per-character font matching (`@font-face` `unicode-range` + glyph-coverage fallback, issue #158) already
walked a box's *declared* `font-family` stack looking for a family that covers a given character.
PeachPDF now adds the last-resort step every browser engine also performs (CSS Fonts 4 §5's final
"system fallback"): when nothing in the declared stack covers a character, it searches every OTHER font
registered with the `PdfGenerator` instance — system-discovered fonts plus anything added via
`AddFontFromStream` — for one that does, closing issue #172.

- *Before:* a character no declared family covered always rendered as a `.notdef` (tofu) box, even if
  some other font registered with the document (or discovered on the host) could render it.
- *After:* PeachPDF tries every other font it knows about before giving up. When more than one covers the
  character, the one whose own coverage best overlaps the character's Unicode script wins, so a font
  built for that script is preferred over one that merely has an incidental glyph in range; a character
  with no script of its own (punctuation, digits) or a tie between equally-good candidates falls back to
  alphabetical family-name order.

Some previously-tofu characters will now render using a different-looking font than the rest of the run —
whichever registered or system font happens to cover them, not necessarily one chosen for stylistic
consistency with the declared `font-family`. A document relying on the old behavior (e.g. to visibly flag
an unsupported character) would need to check coverage itself before rendering.
