# `white-space: break-spaces` can end a line after every preserved space

A run of preserved spaces was one word, and the Unicode line breaking algorithm has no break before a space (LB7), so a long run could not wrap.

- `CssBox.AppendWordsFromText`: under `white-space-collapse: break-spaces` each preserved space is a word of its own. The second and later spaces of a run
  carry a break opportunity before them (css-text-3: a break after each preserved space); the first keeps the algorithm's own answer (none after a letter).
  Layout already stopped them hanging (`PreservedSpacesHang`), so a space that does not fit wraps to the next line.
- Other preserving modes (`pre`, `pre-wrap`) are unchanged: one word per run, which hangs.
- Evidence: WPT css-text +16 (break-spaces-*, ws-break-spaces-applies-to-*, white-space-intrinsic-size-001...), none lost; `LayoutPaintCoverageTests` compares
  `break-spaces` against `pre-wrap` on a run of 60 spaces in a 50px box. `.claude/` notes aside, the `break-spaces` limitation sentence in the SVG text
  docs is about SVG text and is unchanged.
