# `text-justify` in `PeachDrawing.Text.Layout`

`ParagraphStyle.TextJustify` (`Auto`, `None`, `InterWord`, `InterCharacter`) says where a justified line gets its room.

- **The room is a flag per glyph, not a per-space number.** `FindJustificationOpportunities` walks the line's clusters in drawing order and flags the
  cluster-ending glyphs the room goes after; `PlaceRun` adds the equal share to those advances. Word spacing is separate, so the room is *in addition*
  to it. The last cluster of a line never gets room (except a no-break space, as before), and a tab is a wall in the walk.
- **The boundary test is symmetric in the two clusters and uses raw Script, not the resolved one.** Han/Hiragana/Katakana/Bopomofo/Yi (plus U+30FC, which is
  Common) on either side make `Auto` add room; the resolved script would have made the CJK punctuation next to a Han letter qualify, and Chrome does not
  space that. `InterCharacter` skips a boundary the joining form says is joined (`Init`/`Medi`/`Med2` of the earlier character), so Arabic is never pulled apart.
  The offsets are the clusters' first characters even when a right-to-left run reverses the glyph order, hence the `Math.Min`.
- **Letter spacing now drops `liga`, `dlig` and `hlig`** (CSS Text 3), keeping `rlig` and `calt`. The shaped-piece cache is per (start, end) of one paragraph,
  so the setting cannot leak between styles. Room added by justification cannot do this (the text is shaped first): recorded in the gap file.
- Fixtures: the bundled JP subset (its glyphs are not needed: a missing glyph still gives a cluster and an advance) and the Arabic and Hebrew subsets. Source
  Sans 3's only `liga` ligatures are `ff`, `ft` and `fft`, so the ligature test uses `ff`.

Evidence: `ParagraphJustifyTests` (16), the existing layout and spacing tests unchanged.
