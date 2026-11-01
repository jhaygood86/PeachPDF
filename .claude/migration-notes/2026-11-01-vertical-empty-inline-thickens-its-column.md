# An empty inline thickens its column in a vertical writing mode

Before: in `writing-mode: vertical-rl`/`vertical-lr`, an empty inline element with a larger `font-size` (`text<span style="font-size:40pt"></span>more` at 10pt) left its column sized from the 10pt text alone, where the same span holding a character made the column as thick as its own line-height.

Now: the empty inline's line-height counts toward the column it sits in (the column of the word that follows it, or the last column for one after the last word), as it already did in horizontal writing modes.
