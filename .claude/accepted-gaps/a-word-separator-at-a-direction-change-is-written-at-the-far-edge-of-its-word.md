# A word separator at a direction change is written at the far edge of its word

`FragmentPainter.PaintWordSeparator` anchors the space glyph it writes for a word separator at the edge of
the word before it - but only when that word sits on the side the following word's own `BidiLevel` expects
(`FragmentPainter.PrecedingWordEdge`). Otherwise it places the glyph against the word that follows the
separator, on that side: the start edge of a left-to-right word, the end edge of a right-to-left one. Inside
a run of one direction that is the visual gap. Where the direction changes across the separator it is not: in `hello <span dir=rtl>שלום עולם</span> world` the space
after `hello` belongs to the paragraph (level 0) but is placed by `שלום` (level 1), at the right edge of the
right-to-left run, where the space before `world` also sits. The visual gap between `hello` and `עולם` holds
no glyph.

The content stream is still in logical order (`hello␠שלום␠עולם␠world`), so readers that take text in stream
order get it right; only position-aware extractors see the glyph in the wrong gap. The glyph has no ink.

Placing it correctly needs the separator's own resolved level (UAX #9 resolves the white space between two
runs to the embedding level) and the position of whichever word is its visual neighbour on that side, which
can be the far end of the other run and in another box's fragment. Paint reads neither, so this was left
out of the change that started writing separators. Not a CSS deviation; nothing on the page is affected.
