# An inline on a `::first-line` line sizes the line from its own font (#1318)

**Gap:** on a line styled by `::first-line`, an inline element contributes its own cascaded font and
`line-height` to the line box, not the ones it inherits from `::first-line`.
`#p::first-line { font-size: 10pt }` on `<p style="font-size: 40pt; line-height: 1.2">text<span>x</span>more</p>`
gives a 48pt first line where 12pt is expected; `text more` gives 12pt. An empty `<span></span>` in the
same place does the same since #1310. CSS Pseudo-Elements 4 §3.1.1 has the parts of an element on the
first line inherit from `::first-line`, and CSS 2.1 §10.8.1 sizes the line from those inline boxes.

**Where:** `CssLayoutEngine.LineBoxContributionOf` swaps in `CssRect.FirstLineStyle` for the strut and
the word's own box, but walks the word's inline ancestors with their own `ActualFont`/`ActualLineHeight`.
`FlowBox`'s `PlaceEmptyInline` walks an empty inline and its ancestors the same way, measured in review of
#1310.

**Why out of scope:** it predates #1310, which only matches the existing walk. Fixing it means knowing, per
inline on the first line, whether its font is inherited (and so replaced by `::first-line`) or declared on
the element, which the layout pass does not have.
