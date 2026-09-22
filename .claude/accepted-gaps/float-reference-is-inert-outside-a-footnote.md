# `float-reference` is inert outside a `float: footnote` source

CSS Page Floats' `float-reference: inline | column | region | page` parses, cascades and round-trips
on every element, and `@supports (float-reference: column)` reports true. Exactly one consumer reads
it: a `float: footnote` source, where `column` routes the note into the note area of the column its
reference landed in.

- **On any other element it does nothing.** PeachPDF implements no page floats at all - there is no
  `float: top`/`bottom`/`block-start`/`block-end` - so there is no float for a reference to
  reposition. It is registered as a real property anyway because it *is* one (it applies to all
  elements), and because hand-parsing the keyword inside the footnote detach pass would be a second
  parser for a grammar the CSS-OM already owns.
- **`region` is accepted and behaves as `page`.** There is no CSS Regions support, so a region
  reference has nothing to resolve against. Accepting rather than rejecting it keeps the CSSOM honest;
  a footnote declaring it gets the page's area, the same fallback `inline` takes.
- **`inline` must keep meaning "the page's area" for a footnote.** It is the property's initial value,
  so any other reading would change every existing footnote document. A footnote has no inline note
  area to fall back to, which is what makes that the right answer rather than a shortcut.

Filed as [issue #1269](https://github.com/jhaygood86/PeachPDF/issues/1269).
