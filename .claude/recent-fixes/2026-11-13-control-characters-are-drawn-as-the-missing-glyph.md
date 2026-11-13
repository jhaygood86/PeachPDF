# A control character is drawn as the font's missing glyph

css-text-3 (white-space processing): a control character other than tab and the segment breaks "must be rendered as a visible glyph". Found by `css/css-text/white-space/control-chars-*` (31 of 63 failing): a C0 control, DEL and U+0085 drew nothing and took no room.

## Two causes, in different packages
- **`PeachDrawing.Core/TextMeasurement.cs`** dropped every character below U+0020 other than tab and LF from the measured run (`// HACK: Unclear what to do here`), so the control never got a glyph or an advance. Now only CR is dropped (half of a line break); the rest are measured.
- **`PeachDrawing.Text` `GsubShaper.MapToGlyphs`**: the bundled font's cmap maps many C0 controls to blank glyphs, so even a measured control drew nothing. A Cc code point other than tab, LF and CR (C0, DEL, C1) now shapes to glyph 0, the font's missing-glyph shape - the same shape the C1 controls already got because the font does not map them.

Neither fix alone is enough: with only the shaper change U+007F drew and U+0001 did not.

## Not done
- U+000B, U+000C and U+0085 are still eaten: the engine splits words on `char.IsWhiteSpace` (some 36 sites) and these count as white space there, but CSS document white space is only space, tab and the segment breaks. A CSS-specific predicate is the fix; it touches word splitting, first-letter splitting and more, and wants its own pass.
- `control-chars-00D` (a CR run treated as a space) is untouched.

## Evidence
css-text +27 (the whole C0 range except the above), 0 lost anywhere; new `ControlCharacterShapingTests`, the two `TextMeasurementTests` that pinned the old drop rewritten; all suites green.
