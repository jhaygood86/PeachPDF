# Arabic: per-occurrence /ActualText for dotted letters; the space before a trailing number

Issue #2451 (follow-up to #2418, #2420).

## Sibling letters shared one ToUnicode destination

Noto Sans Arabic draws BEH/TEH/THEH/NOON/YEH... as one dotless body glyph plus dot glyphs the shaper emits
with an empty cluster. ToUnicode has one destination per glyph, so the body glyph extracted as whichever
letter was drawn last. `PdfGraphicsRenderer.DrawString` now wraps any run that holds a glyph with
`ClusterLength <= 0` in `/Span <</ActualText ...>> BDC ... EMC` carrying the run's logical text (`logicalText`
when it is the aligned source, else the drawn string). Runs without such a glyph are byte-identical. The
dots still map to U+200B as the fallback for readers that ignore `/ActualText`.

A separator that follows such a run is no longer written with a bare `Tj` at the pen (the `EMC` changes the
content length `TryShowAtPen` checks), so it gets its own `Td`.

## The space between Arabic and a number after it was placed at the far end of the run

`ApplyBidiReordering` kept each run's trailing gap after the run in visual order. For `<arabic> 42` the number
(level 2) splits the level-1 run, and the gap between the Arabic word and the number ended up between the
Arabic and the full stop. `PlaceGapsBetweenRuns` now leaves the gap between two reading-order neighbours when
reordering puts them side by side (the number visually left of the word it follows), and otherwise keeps the
old placement. The painter (`PrecedingWordEdge`) anchors the separator on the side the word really sits on when
it is right next to its predecessor, falling back to the paragraph's direction (the `42 <arabic>` rule from
#2420) otherwise.

## Evidence

`WordSeparatorGlyphTests` (both paragraph directions), `WordSeparatorPdfOutputTests` (`/ActualText` with the
Arabic subset). PeachBrackets.Engine.Tests, PeachPDF.Tests and PeachDrawing.PDF.Tests pass on net10.0.
