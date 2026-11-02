# Tagged PDF takes alternative text, language and table details from the document

**Before:** with `EnableTaggedPdf`, a `Figure` had `/Alt` only from the `alt` attribute (so an inline `<svg>` or an `<img>` named by `aria-label` had none), a `Formula` never had one, a `lang` attribute below `<html>` was not recorded, and table cells carried no attributes.

**Now:** `/Alt` also comes from the element's accessible name - an `aria-label`, an `aria-labelledby` target, an inline SVG's `<title>`, a formula's `alttext` or a plain English reading of its structure (`x squared plus 1`) - and `alt` still wins. An element that sets `lang` gets `/Lang`. A `TH` has a `/Scope` of `Column` or `Row`, and a cell with `colspan` or `rowspan` has `/ColSpan` and `/RowSpan`, as `Table` attributes. Documents without these are tagged exactly as before. Rendered pages are unchanged.

**Why:** assistive technology reading a PDF gets the same names, languages and table positions it gets from the same document on screen, and PDF/UA asks for them.
