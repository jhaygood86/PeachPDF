# `PeachDrawing.Text.Layout`: what the first slice leaves out

The paragraph layout API (`ParagraphBuilder`, `Paragraph`, `ParagraphLayout`) does line breaking (UAX #14 with the CSS tailorings), bidi
reordering, alignment, carets, hit testing and selection boxes. Tracked in [#1417](https://github.com/jhaygood86/PeachPDF/issues/1417), it does
not yet have:

- justification and `text-align-last`;
- font fallback (a character the run's face lacks draws the missing-glyph shape);
- letter and word spacing, tab stops, `text-indent`, hyphenation, a line limit with an ellipsis;
- the host-driven tier (`LineFlow`, `FlowCursor`, `LineSpace`);
- inline atomic boxes.

PeachPDF does not consume the API, and that is deliberate rather than pending: `FlowBox` interleaves the wrap decision with float intersection,
speculative line-height growth, fragmentainer break tokens and `::first-line`, so a wholesale replacement is not planned. The library owns "break
the next line given an available range" for embedders without those constraints; PeachPDF stays the host of its own retry loop. The pure helpers
(justify distribution, overflow-wrap candidate search, the three copies of bidi line reordering) are the candidates to move behind the library
one at a time, each with its characterization tests kept in PeachPDF.

Two behaviours to know before changing the layout: hanging spaces are cut from a line's runs (they are in `LineBox.Range`, not drawn), and a piece
is shaped per (atom, range), so a break between two clusters loses kerning across it, which is what a break should do.
