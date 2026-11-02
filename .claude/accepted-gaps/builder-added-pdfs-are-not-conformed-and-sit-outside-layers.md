# PDFs added with `Append`/`Prepend` are not re-conformed (object copy), and file layers skip them

`IDocumentBuilder.Append`/`Prepend` write an existing file's pages into the generated document. Three limits follow from
doing that inside the document's own composer rather than as a second pass:

- **`PdfCopyMode.ObjectCopy` pages keep their own content.** Their fonts, colour spaces and transparency are not brought
  into line with the document's PDF/A or PDF/X profile, so an object-copied non-PDF/A file in a PDF/A document makes the
  *document* claim a level some of its pages do not meet. `Redraw` (the default) draws the page through the document's
  profile and is the mode to use when the claim matters. The other limits of object copy are in
  [the object-copy note](pdf-objectcopy-leaves-document-level-features-behind.md).
- **File layers (`OverlayFile`/`UnderlayFile`) are drawn on the generated pages only.** Their target pages are read against
  the whole document (so `"3"` can be the first generated page after two added ones, and `"2-z"` ends at the last page,
  added or not), but `FileLayerPlan.Paint` runs inside `RenderPagesCore`, which an added page never goes through. A layer
  asked onto an added page is silently not drawn there. Drawing it would mean routing added pages through the same plan.
- **The added file's document information and attachments are not taken** (title, author, keywords, embedded files): the
  generated document owns them, and `PdfComposerOptions` is fixed before the first page. `PdfOperation.Save` takes the
  first file's; `AppendTo` does not.

A form field of an added file keeps its name unless the document or an earlier added file has it, then it becomes
`name_2`; the generated document's own fields (`PeachPDFField…`) are not looked at, so an added field named exactly like one
of them would be a second field with the same name. Not reachable in practice since the generated names carry a prefix.

More limits of the same approach, found in review:

- **Destination names are only unique among added files.** `AppendState.DestinationNames` knows the names earlier `AppendTo` calls took, not the anchors the HTML path writes (`HtmlDocument` per page) or a file layer's destinations, and `AddNamedDestination` keeps the last one written. An added file's destination `intro` and a generated `id="intro"` anchor therefore shadow each other. Fixing it means seeding the set from the composer's own names.
- **Added pages are not tagged**, so a document that claims PDF/UA or PDF/A-a is not accessible on them; the structure tree is built from generated pages only.
- **Redraw warnings are not surfaced** by the builder (`PdfOperation.Warnings` is set, but the operation is not exposed after generation).
- **The outline of an added file joins the document's single outline**; an added file whose first entry is nested deeper than level 1 nests under the generated heading before it.
