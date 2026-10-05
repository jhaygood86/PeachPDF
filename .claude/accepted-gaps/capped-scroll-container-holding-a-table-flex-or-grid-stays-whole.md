# A capped scroll container holding a table, flex or grid container stays whole

A scroll container with an auto height and a `max-height` breaks between its lines under its cap, but one that
holds a table, flex container or grid container (anywhere in its in-flow content) is still treated as monolithic,
the way every capped box was before. A browser breaks it between rows or lines; PeachPDF moves it whole to the next
page, or slices it when it is taller than a page.

Why: content past the cap is laid out without taking a break, so the siblings after the box are never placed on a
page already emitted. That is held back in `FlowBox` (per line, `CappedScrollContainer.IsPastCap`) and in
`CssBox.LayoutBlockChild` (per block child, which detaches the fragmentainer). A table, flex or grid engine takes its
own break decisions, which neither hook sees: a row break past the cap would end the pass with the box's siblings
unplaced, which is the failure the earlier retry-based attempt hit. `CappedScrollContainer.MustStayWhole` keeps such a
box monolithic, and `CappedAutoOverflow_HoldingAnEngineOfItsOwn_StaysMonolithic` and
`ACappedBoxHoldingATable_StaysWhole` pin it.

Closing it means making those engines consult `CappedScrollContainer.IsPastCap` before a break, and dropping
`MustStayWhole`. Tracking issue: [#1645](https://github.com/jhaygood86/PeachPDF/issues/1645).

Documented in the scroll-container paragraph of `docs/html-css-support.md`.
