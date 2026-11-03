# The tokenizer, the frameset modes and foreign-content attribute fixups are not the standard's

Tree construction follows the HTML Standard's insertion modes (see [the document modes note](../recent-fixes/2026-11-03-document-modes-and-template.md)); three parts of parsing do not:

- **The tokenizer is MimeKit's `HtmlTokenizer`**, not the standard's state machine. The tree builder cannot tell it to switch to RCDATA, RAWTEXT, script data or PLAINTEXT, there are no CDATA sections in foreign content, and
  no script data escape states. Text in `title`, `textarea`, `style` and `script` is handled as raw text on the tree side (it goes into the open element whatever the mode is), as far as MimeKit delivers it as one run.
- **Framesets.** `frameset` and `frame` are ordinary elements; the "in frameset" modes and the frameset-ok flag (a `frameset` after the body replacing it) are not implemented. Frames are hidden anyway.
- **Foreign content.** SVG element names are given their mixed case (`clippath` is `clipPath`), but SVG attribute names (`viewBox`...) and MathML `definitionURL` are not, and the MathML text integration points and `annotation-xml` encoding checks are a fixed name list.
- **Customizable select.** Richer content inside a `<select>` is dropped, as the classic "in select" mode does.

Tracked in issue 2148.
