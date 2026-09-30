# `PeachDrawing.Text.Layout`: limits of what it does

What the paragraph layout API does not do, or does in a simplified way, on purpose. Read before treating one of these as a defect.

- **Layout behaviours.** Hanging spaces are cut from a line's runs (they are in `LineBox.Range`, not drawn), and a piece is shaped per (atom, range), so a break between two clusters loses
  kerning across it, which is what a break should do.
- **Font fallback** asks about a user-perceived character by its first code point only: a ZWJ emoji sequence, or a base plus a variation selector or modifier whose first code point the
  run's face maps, is never sent to the fallback, so a missing joiner, modifier or emoji form shows the face's missing-glyph shape where a covering face could have drawn the sequence.
- **Spacing and justification.** Letter spacing is added after the last glyph of each grapheme cluster and turns off the optional ligatures of its text; the room justification adds does not
  (the text is shaped before it is known), where CSS Text 3 says an agent should. `text-justify: auto` is spaces plus Han, Hiragana, Katakana, Bopomofo and Yi letter boundaries (not the clustered
  scripts of South-East Asia, and no kashida for Arabic).
- **Tab stops** are measured along a line in the order the text is written, so a tab that follows right-to-left text in a left-to-right line is placed as if that text were where it is in memory,
  not where it is drawn. In a `LineFlow` they are measured from the start edge of the line's space plus its indent, not from the edge of a block that a float narrowed the space of. Justification
  widens spaces only, so text after a tab in a justified line leaves its stop.
- **Hyphenation:** `hyphenate-limit-last` offers `always` only (a paragraph has no columns, pages or spreads), and in a `LineFlow` the next line's room, which it is tested against, is guessed from the width of the space being laid out, since the next space is not known. A word is hyphenated by its longest run of letters up to 128 UTF-16 units, so one with digits or an apostrophe in it is left whole, as the pattern engine does; the
  patterns are the TeX ones for about seventy languages, embedded Brotli-compressed, so on a host with no working Brotli decoder and none registered (`System.IO.Compression.BrotliStream` throws in WebAssembly, and `PeachDrawing.Text.Brotli` is the ready-made managed decoder for it) `Auto` finds no points. The pattern engine's own
  minimums are a floor under `HyphenateLimitChars`. A soft hyphen inside a ligature or a kerned pair stops the ligature or kern, since the text is shaped with it before its glyph is dropped.
- **Ellipsis:** the cut is at a boundary between grapheme clusters, not at a word (the CSS `line-clamp` algorithm leaves the choice to the agent). `text-overflow: ellipsis` takes a single
  ellipsis at the end of the line, not the two-value form (one at each end). An ellipsis wider than the room is drawn anyway, as the only content of a line that overflows; CSS UI 4 says it is
  clipped, which is the caller's to do. A caret in the hidden text is drawn after what is drawn. `LineFlow` does not apply `MaxLines`; the caller decides how many lines there are.
- **Inline boxes:** alignment is the baseline family (`baseline`, `middle`, `text-top`, `text-bottom`, `top`, `bottom`, with a shift for `sub`, `super` and lengths); the percentage form of
  `vertical-align`, and alignment relative to an enclosing inline box other than the run the box is in, are the caller's to resolve, since a run's own metrics are all the layout has. A box is a wall
  for justification (CSS Text 3 6.4.5 leaves atomic inlines to the agent) and a break is allowed on both sides of it, where a caller that wants a box glued to the word before it must put a word
  joiner (U+2060) next to it. A combining mark or joiner typed straight after a box makes one grapheme cluster with it, so there is no caret stop between them.
