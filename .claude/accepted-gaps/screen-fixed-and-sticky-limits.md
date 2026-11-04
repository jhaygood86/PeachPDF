# Fixed and sticky boxes on a screen surface: what is not done

`position: fixed`, `position: sticky` and `background-attachment: fixed` follow the viewport a host reports with `HtmlDocument.SetViewport`. Left out on purpose:

- **Sticky inside a nested scroller that scrolls.** The engine has no nested scrolling (an `overflow: auto/scroll` box is clipped, never scrolled), so a sticky box inside one does not stick; the nearest scrollport is always the viewport.
- **Fixed offsets are measured from the page area's corner**, which is the content area inside `DocumentMargin`, not the viewport edge. With no margin (the default) they are the same.
- **Dirty regions cover a displaced box's rectangle plus a 24pt bleed.** A shadow, outline or overflowing descendant farther out than that is repainted late.
- **A document with more than one fragmentainer** (only a paged one) has no viewport to move: text runs, links and form fields are displaced for the single fragmentainer of a continuous document.
- **Not tracked as a GitHub issue yet.** These are limits of non-interactive painting rather than spec deviations a document author hits by accident; file issues if one is reported.
