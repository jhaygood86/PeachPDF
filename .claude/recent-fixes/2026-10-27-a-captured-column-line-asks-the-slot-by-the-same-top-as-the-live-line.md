# A heading in a columns container just above the foot was in no fragment of any page (#1963)

Before: with `<div style="height:150pt">` then `<div style="column-count:2"><h3>…</h3></div>` on a 300pt x 200pt page
(foot at 180), the heading's text was in no page's fragment tree. The container starts 0.3pt above the foot, the
heading's top margin carries its line to 179.7-191.7 in the second column.

What was misread: it looked like a pruning difference (the generated corpus had a few of those) and then like the
near-the-foot exemption (`FreshPageBandHeight`). The pruning parity check does not throw on it and `straddles` was false
for the right reason. A hook on `ClaimsLine` showed both slots being asked and both saying no:

- slot 0 walks the live line: `lineTop = rect.Top + InkRiseAboveLineTop` = the line's flow top, 179.71, whose slot is 1
  (`SlotStartingAt` counts a top within 0.5pt above a boundary as the later slot);
- slot 1 walks the column's captured copy: `InkRiseAboveLineTop` returned 0 for a snapshot, so `lineTop` was the ink's
  top, 178.98, whose slot is 0.

Each slot declined it for the other. Only a heading's ink rise of 0.73pt plus the line's 0.29pt before the boundary
made the two tops fall on different sides, hence a window under 1pt wide.

Fix: `InkRiseAboveLineTop` now reads the rise for a captured copy off the live line box's own pair of tops (the line's
flow top minus its live rectangle's top). How far glyphs rise above their line is a property of the line, the same
wherever it sits. The heading is then drawn on the next page, its ink 1pt into the top margin (PDF text at y=19.0-32.1).

Evidence: `AHeadingInAColumnsContainerStartingJustAboveTheFoot_IsDrawnOnAPage` (2 and 3 columns, spacer swept 146-162pt
in 0.25pt steps) fails without the change (`lost: w2_1..w2_4`); Engine.Tests 16538 and PeachPDF.Tests 1499 on net8.0
(Debug). A 300-document generated corpus had 22 documents with words in no fragment before; not re-run on this branch.
