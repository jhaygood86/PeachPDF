# `PeachDrawing.Text.Layout`: what the layout API still leaves out

The paragraph layout API (`ParagraphBuilder`, `Paragraph`, `ParagraphLayout`) does line breaking (UAX #14 with the CSS tailorings), bidi
reordering, alignment, carets, hit testing and selection boxes, font fallback, letter and word spacing, justification
(`text-justify`) with `text-align-last`, `text-indent`, tab stops and hyphenation (`hyphens`, `hyphenate-character`, the four `hyphenate-limit-*`). Tracked in [#1417](https://github.com/jhaygood86/PeachPDF/issues/1417), it does not yet have:

- a line limit with an ellipsis;
- the host-driven tier (`LineFlow`, `FlowCursor`, `LineSpace`);
- inline atomic boxes.

PeachPDF does not consume the API, and that is deliberate rather than pending: `FlowBox` interleaves the wrap decision with float intersection,
speculative line-height growth, fragmentainer break tokens and `::first-line`, so a wholesale replacement is not planned. The library owns "break
the next line given an available range" for embedders without those constraints; PeachPDF stays the host of its own retry loop. The pure helpers
PeachPDF has for the same jobs are written over its word model (`CssRect` words, `CssLineBox`), not over text, so they do not move behind the
library: `ExpandTabs` and `GetLineTextIndent` (tab stops and indent, kept in PeachPDF with their tests), `ApplyJustifyAlignment` and
`IsJustificationOpportunity`, the overflow-wrap candidate search, the three copies of bidi line reordering.

Two behaviours to know before changing the layout: hanging spaces are cut from a line's runs (they are in `LineBox.Range`, not drawn), and a piece
is shaped per (atom, range), so a break between two clusters loses kerning across it, which is what a break should do.

Font fallback asks about a user-perceived character by its **first code point only**: a ZWJ emoji sequence, or a base plus a variation
selector or modifier whose first code point the run's face maps, is never sent to the fallback, so a missing joiner, modifier or emoji
form shows the face's missing-glyph shape where a covering face could have drawn the sequence. Letter spacing is added after the last glyph
of each grapheme cluster, and it turns off the optional ligatures of its text; the room justification adds does not (the text is shaped before it is known),
where CSS Text 3 says an agent should. Justification treats `text-justify: auto` as spaces plus Han, Hiragana, Katakana, Bopomofo and Yi letter boundaries
(not the clustered scripts of South-East Asia, and no kashida for Arabic).

Tab stops are measured along a line in the order the text is written (logical order), so a tab that follows right-to-left text in a left-to-right
line is placed as if that text were where it is in memory, not where it is drawn.

Hyphenation: `hyphenate-limit-last` offers `always` only (there are no columns, pages or spreads in a paragraph). A word is hyphenated by its longest run of letters, so
one with digits or an apostrophe in it is left whole, as the pattern engine does; the patterns are the TeX ones for about seventy languages, embedded Brotli-compressed, so in a
WebAssembly host (which has no Brotli decoder) `Auto` finds no points. A soft hyphen inside a ligature or a kerned pair stops the ligature or kern, since the text is shaped with it
before its glyph is dropped.
