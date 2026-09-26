# `text-indent` and tab stops in `PeachDrawing.Text.Layout`

`ParagraphStyle.TextIndent` (`TextIndent(Length, Hanging, EachLine)`) and `ParagraphStyle.TabSize` (`TabSize.FromSpaces`/`FromLength`, default 8 spaces).
Design points and what running it showed:

- **Line breaking became a pure per-line function.** `ParagraphLayoutBuilder.FitLine(paragraph, start, room, pen)` finds where the line starting at
  `start` ends from the paragraph and its arguments alone; `Build` loops it. That is what an indent that differs by line needs (whether a line is
  the first, or follows a forced break, is a function of its start offset: `Paragraph.IndentAt`) and it is the shape the host-driven `LineFlow` tier will
  expose, so the next line never depends on a mutable cursor.
- **The indent is room taken from the line, then alignment runs in what is left.** For a right-to-left paragraph the indent is on the right, so the
  area is `[0, width - indent]` and for left-to-right `[indent, width]`. Unlimited widths use the widest `indent + line width`. An indent wider than
  the paragraph is clamped to zero room, never negative: with a negative room the old emergency-cut loop never ended, because even an empty remainder
  (width 0) is wider than a negative number.
- **A tab is an atom of its own** and its width is the distance to the next stop from the pen, measured from the paragraph's start edge in logical
  order (`Measure(start, end, pen)`, `TabAdvance`). `Assemble` computes the tab widths of a line along the text before it reorders the runs, then gives
  a tab a `PlacedRun` with **no glyphs**, so a host that draws every glyph never draws a tab's missing-glyph shape. The consequence, documented, is that
  the sum of a tab run's glyph advances (none) is not its width. Tab stops do not use the "minimum tab width" some browsers add.
- **`LargestFit` no longer costs the length of the word.** The emergency cut used to build a list of every grapheme boundary of the remainder and re-measure
  the whole remainder for each line: quadratic in a long word (a 60,000 character word cut into many lines now takes about a second). It probes
  outward (gallops) and bisects inside the last interval, so the cost follows the line it finds.
- A one-cluster word that is wider than the line no longer becomes an `Emergency` line of its own followed by a line that starts with its trailing spaces;
  it overflows in place, like an uncuttable word does.
- `FitLine` skips all measuring when the paragraph does not wrap (a width of infinity, or `NoWrap`).

Not moved out of PeachPDF: `ExpandTabs` and `GetLineTextIndent` work over the word model and stay with their characterization tests. Evidence:
`ParagraphIndentAndTabTests` (36 tests: which lines get the indent, RTL, alignment and justification in the remainder, negative and oversized indents,
tab stops with spacing and indent, carets and selection across a tab, hostile widths and a huge word), the existing layout tests unchanged, and
Debug and Release runs of `PeachDrawing.Text.Tests`.
