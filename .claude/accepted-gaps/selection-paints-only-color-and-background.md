# `::selection` paints only `color` and `background-color`

`HtmlDocument.SetSelectedRanges` (opt-in `EnableSelectionStyling`) paints a selection in the document's `::selection` rules, but reads only
`color` and `background-color` from them. CSS Pseudo-Elements 4 §3.2 also allows `text-decoration*`, `text-underline-*`, `text-shadow`,
`-webkit-text-stroke*`/`-webkit-text-fill-color`, `fill`/`stroke` and `caret-color`; those declarations are cascaded and then ignored.

Also not done, all deliberately, to keep the first pass to what cannot change layout:
- **Highlight inheritance through an intermediate rule.** A selection style is seeded from its element's own style, and an element with no
  rule takes its nearest ancestor's. A declared `color` on an outer `::selection` is lost on a child whose parent has rules of its own
  (§3.2's "inherits from the parent's highlight pseudo").
- **Highlight height.** The fill is as tall as the word's own rectangle, not the full line box (browsers fill the line box).
- **Named highlights share the same limits.** `::highlight(name)`, `::target-text`, `::spelling-error` and `::grammar-error` are painted (`HtmlDocument.SetHighlight`),
  but only `color` and `background-color`, with no inheritance through an intermediate rule; `::target-text` is not applied by the engine itself
  (a host sets it), and a highlight on a text edit is dropped, not moved.
- **Vertical writing modes and transforms:** a selected word in vertical text is painted unselected.
- **Duplicate text:** a word whose colour the selection changes is drawn three times (outside, inside, outside, each clipped), so a PDF
  written with a selection holds that text more than once. Selection is for interactive output.

Closing the first needs the decoration/shadow/stroke painters to take a clipped range; the second needs a tracked "declared" bit per property
on the shadow style rather than the equality test against the element's own colour used today.

**Hairline seams under MuPDF.** The highlight is one rectangle per word plus one per selected gap, which abut exactly; MuPDF anti-aliases the shared edges and shows faint vertical hairlines between them (PDFium does not). Overlapping them would double a translucent highlight colour, so they are left abutting; coalescing a line's rectangles in the painter would remove the seams.
