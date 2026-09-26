# The paragraph layout API (`PeachDrawing.Text.Layout`)

`ParagraphBuilder` -> `Paragraph` (bidi, scripts, joining, UAX #14 opportunities, worked out once) -> `Paragraph.Layout(width)` -> an immutable
`ParagraphLayout` of `LineBox`es of `PlacedRun`s. The design is in the plan (section 7.2); what running it and reviewing it showed:

- **Atoms.** The text is cut into atoms (one run style, one bidi level, one script; line terminators excluded), and a piece of an atom is what is
  shaped, cached by `(start, end)`. Line breaking measures the words between opportunities piece by piece and the lines are then shaped whole,
  so `LineBox.Width` can differ slightly from the width a line was fitted at where a font kerns across a space. That is a known cost of shaping
  without a break, not a bug; the class remarks say so.
- **Hanging spaces are cut from a line's runs** (kept in `LineBox.Range`, not counted, not drawn), which also sidesteps rule L1 for trailing
  whitespace: the levels the reorder sees end at the last non-space character. Selection boxes and hit tests therefore never land in them; a
  caret after them is placed at the line's end in the *paragraph's* direction (a review found it used the last run's direction).
- **A hit test's affinity follows the returned index**, not the visual side that was hit: the right edge of a right-to-left line is its logical
  start. An emergency cut chooses a line like a soft break does.
- **A cluster can be several glyphs** (a base and its marks), so `PlacedRun` caret positions are taken from the union of the cluster's glyphs;
  setting them glyph by glyph let the zero-advance mark overwrite the cluster's start.
- **Text with paragraph separators is one bidi paragraph** (one base direction); a caller wanting each paragraph to detect its own builds
  several `Paragraph`s. `default(ParagraphStyle)` is left-to-right (only the constructor sets `Auto`), which is why the wrap switch is `NoWrap`.
- The shaped-piece cache is capped and cleared when full: a resize drag lays out at many widths.

Evidence: 570 tests in `PeachDrawing.Text.Tests` (40 for layout, including Arabic joining, Devanagari syllables, RTL carets and hit tests), 95%
line coverage of `Layout/`, and a read-only review whose findings are fixed above. See
[the omissions](../accepted-gaps/text-layout-api-first-slice-omissions.md).
