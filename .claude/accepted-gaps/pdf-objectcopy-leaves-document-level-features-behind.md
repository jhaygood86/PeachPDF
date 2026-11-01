# `PdfCopyMode.ObjectCopy` copies a page's own objects, not the document features around them

`PdfObjectCopier` clones what a page reaches: its content streams (still encoded), resources, boxes, rotation,
annotations, and the form fields its widgets belong to. It deliberately does not carry:

- **The structure tree** (`/StructTreeRoot`, `/StructParents`, `/StructParent`). A tagged source stops being tagged;
  `Redraw` can keep a page's tagging through `PdfTagSession`, the object copy cannot because the tree's elements
  point at marked-content ids that would need rewriting together with the tree.
- **Optional content** (`/OCProperties`): a layer of the source is a normal object here, so content that depends on a
  viewer's layer settings is shown unconditionally.
- **Scripts and risky actions**: a link or widget action of type `/JavaScript`, `/Launch`, `/ImportData` or
  `/SubmitForm` (or one with a `/JS` entry) is removed instead of copied, because merging someone else's file must
  not smuggle behaviour into the output. The same goes for page-level `/AA`.
- **Signatures**: copying changes the byte ranges a signature covers, so a signed field comes across unsigned in
  effect; it is not re-signed.
- **A form's calculation order** (`/CO`) and `/XFA`.

Two things are limited rather than absent:

- A page taken more than once has its widgets once: the field exists once, and a widget is part of its field.
- A layer can be drawn on an object-copied page only when the page has no rotation or crop box and its media box
  starts at the origin, because the layer is painted in the unrotated, origin-zero space of the new page. The save
  throws `NotSupportedException` otherwise; `Redraw` handles those pages.

Named destinations and the outline of the sources are carried, but through the same navigation readers `Redraw`
uses, so they behave identically in both modes: a name that two merged files both define keeps the first file's
destination (the second is stored as `name_2`), and an outline entry whose page was not taken is dropped with only
the entries above it kept as headings.
