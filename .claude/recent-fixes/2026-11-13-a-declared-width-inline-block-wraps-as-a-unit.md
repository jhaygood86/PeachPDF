# A declared-width inline-block moves to the next line as a unit

An inline-block whose content fits one line inside it is flowed into its parent's line, and that path never asked whether the box's
declared margin box fit what was left of the line: two `display:inline-block` boxes of 514px and 482px sat side by side in a 784px
container, the second running off the page. Only the independent-formatting-context path (block content, wrapping content) preflighted.

- `FlowBox` now calls `FitAtomicInlineOnLine` for a box with a resolved declared content width (`ResolveAtomicInlineDeclaredWidth`), using
  the border-box width as the estimate, before its words are flowed. A wrap re-applies the pre-shifted top inset on the new line.
- `auto`-width inline-blocks are untouched (the gap file keeps that remainder).
- Evidence: WPT +9, none lost (flexbox `flex-basis-item-margins-001`, grid subgrid `standalone-axis-size-005/006`, `absolute-tables-013..015`,
  floats `floats-placement-004`, flow `margin-collapse-014`); `LayoutPaintCoverageTests` render both the wrapping and the fitting case.
