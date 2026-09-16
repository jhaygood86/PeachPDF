# A coordinate layout recorded is not always the coordinate paint drew at

`CssLineBox.BaselineY` is documented as "this line's baseline, in the same document-Y space as its
words". It reads like the authoritative anchor for anything that has to sit relative to text, and it
was the first implementation of the underline-baseline fix (issue #1111).

**It is short by the centering offset inside a table cell.** Measured symptom: a
`line-height: 2` cell reported `BaselineY = 17.601` while the `DrawString` call that painted its text
put the baseline at `20.014`. The cause is ordering — the UA stylesheet gives every cell
`vertical-align: middle`, and the pass that centres the cell's content block runs *after* the line
closed and recorded its baseline. Nothing updates it.

Taking it at face value moves every underline in every vertically-centred cell, which no existing test
would have caught, because none pins an underline inside one.

## The rule

**Paint's ground truth is the fragment tree.** A word rectangle in it is what `DrawString` is
literally handed, so anything that must sit a known distance from rendered glyphs derives its anchor
from those rectangles — not from a number `CssLineBox` or `CssBox` recorded during layout. This is the
same contract `docs/architecture.md §6` states as "paint consumes only the fragment tree and must
never read geometry off `CssBox`"; `CssLineBox` is the case where the temptation is strongest, because
the number it offers is named exactly what you want.

Layout's own convention is worth knowing when you do: a word's rectangle is placed a whole **rounded**
`RFont.Ascent` above the baseline, while glyphs are painted from the unrounded
`RFont.TextBaselineOffset`. The two differ by up to half a point, so `wordRect.Y + font.Ascent` is the
*line's* baseline in layout's terms and `wordRect.Y + font.TextBaselineOffset` is the one on the page.
Mixing the two conventions is how a fix that is correct in principle still shifts every document by a
rounding error.

## How to check a candidate anchor

Print it beside `point.Y + font.TextBaselineOffset` from the `DrawString` call for the same line, for
a fixture set that includes a table cell, a superscript, and a line mixing two font sizes. Agreement
on a plain paragraph proves nothing — that is the case where every candidate agrees.
