# A document's language selects the font's language system when shaping

Before: `lang` (the `lang` attribute, `<html lang>`) never matched a font's language-system records, because the table that maps BCP 47 tags to OpenType tags wrote them without the
trailing space a font's tables carry (`TRK`, not `TRK `), so shaping always used the font's default system.

Now: text in a language a font defines its own system for is shaped by it (a Turkish page in a font that leaves out the `fi` ligature for Turkish no longer draws the ligature). Fonts
with no language-specific records, and languages with no system in the font, shape as before.

Check against the last release: `git show <previous-tag>:src/drawing/PeachDrawing.Text/Internal/Text/OpenTypeLanguageTags.cs` returns the tag unpadded and the shaper compared it with the padded one.
