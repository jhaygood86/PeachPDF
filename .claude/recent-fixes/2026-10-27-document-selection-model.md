# DOM-style selection model (`HtmlDocument.Selection`)

Phase 3 of the selection roadmap: `DocumentSelection` - anchor/focus, `Modify(extend, direction, granularity)`, click/double/triple-click helpers,
`SelectionChanged` with dirty regions, `GetElementAt(TextPosition)`. It is a pure model over `TextRuns`; it draws nothing and, with
`EnableSelectionStyling`, forwards each change to `SetSelectedRanges` (Phase 1), reporting the regions to repaint in the shape
`PaintPageRegionAsync` (Phase 2 tier A) takes.

**Decisions.**
- Positions are `TextPosition`s over the text runs, not DOM node/offset pairs. A DOM `Range` over element children needs a node model the
  engine does not expose; the runs are what every other piece of the stack already speaks. `GetElementAt(position)` recovers the element.
- Movement (`Html/Core/Text/SelectionMovement.cs`) works on the runs alone and is logical, not visual: the embedder maps the left arrow in RTL
  text to a backward move. A visual-order caret needs the bidi level runs of each line, which the runs do not carry.
- A position at the end of one run and the start of the next *on the same line* (a run split at a link) is one place, so a character step
  across it moves a whole character; across a line or block it is a step in itself that selects no text yet.
- Up/down keep a goal x (`_goalX`) across consecutive line moves and drop it on any other change; at the first or last line they go to the line
  edge, as a text field does.
- The dirty regions of a change are the old selection's rectangles plus the new one's (inflated by 1pt) plus a caret rectangle for a
  collapsed one, so a caret blink or move costs two thin regions.

**Traps.** `Select(TextRange)` (public, void) and a private `bool` helper overloaded the same name; `a && Select(x)` failed to compile until
the helper was renamed. A selection that ends at the start of the next paragraph copies without the break (`GetText` adds a separator only
before a non-empty piece), which matches what a drag to a paragraph's left edge copies in a browser.

**Not done.** Visual-order movement in bidirectional text, page-by-page (PageUp/PageDown) moves, and editing hooks (they need tier B).

**Evidence.** `DocumentSelectionTests` (13); the whole `PeachBrackets.Tests` and engine suites are unchanged.
