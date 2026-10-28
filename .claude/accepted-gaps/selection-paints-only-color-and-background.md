# Highlights are not painted in vertical text, and a few smaller limits

`HtmlDocument.SetSelectedRanges` and `SetHighlight` (opt-in `EnableSelectionStyling`) paint the selection and the named highlights
(`::selection`, `::highlight(name)`, `::spelling-error`, `::grammar-error`, `::target-text`) from their rules: `background-color` fills the line box
behind the highlighted characters, `color`, `text-shadow` and `-webkit-text-stroke*` restyle the text there, and a `text-decoration` the highlight adds
is drawn under it. An inherited `color` or `text-shadow` an ancestor's highlight declared reaches a child whose own highlight rules do not.

Deliberately not done:
- **Vertical writing modes** (#1970). `TextRunCollector` yields no text runs for a `vertical-rl`/`vertical-lr` box, so there is no position to select or
  highlight and no character edges along the column. Painting it needs the text-geometry model extended to vertical text first (runs, offsets, bounds,
  hit-testing), then the fill and clips on the block axis; upright (CJK) words also need their own advances, not the shaped ones.
  A highlight under a CSS transform follows it (the fill is drawn in the element's transformed space; `HighlightTests` pins that).
- **`fill`, `stroke`, `stroke-width` and `caret-color`** on a highlight (§3.2): not read (no SVG text or caret is painted from the highlight styles).
- **Decoration geometry of a highlight inside an inline box** is the word's own rectangle: a decoration the highlight adds is drawn per word, so
  it starts and ends at word edges and skips the space between two highlighted words.
- **Declared properties are detected by difference**, not tracked: a highlight rule that sets a property to the same value the element already has is
  read as "not declared", and then an ancestor highlight's inherited value (colour, text-shadow) takes over.
- **`text-decoration` is not inherited** into a child highlight, as it is not an inherited property; and `background-color` is not either, so a child
  of an element whose highlight sets a background takes the nearest ancestor's only where it has no highlight rule of its own.
- **Duplicate text:** a word whose colour, shadow or stroke a highlight changes is drawn once per stretch of one look (clipped), so a PDF written with a
  highlight holds that text more than once. Highlights are for interactive output.
- **A highlight is dropped by a text edit**, not moved with the text.

**Hairline seams under MuPDF.** The highlight is one rectangle per word plus one per selected gap, which abut exactly; MuPDF anti-aliases the shared edges and shows faint vertical hairlines between them (PDFium does not). Overlapping them would double a translucent highlight colour, so they are left abutting; coalescing a line's rectangles in the painter would remove the seams.
