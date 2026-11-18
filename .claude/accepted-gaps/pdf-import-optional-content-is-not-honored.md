# PDF import: optional content (layers) is not honored

Neither painting a page (`PdfPageSource.Paint`) nor reading its text (`GetText`) looks at `/OC` marked-content properties or the document's
`/OCProperties`, so a layer the file hides (a watermark, a print-only mark, an alternative language) is drawn, and its text read, as if it were visible.
`BDC`/`EMC` are tracked only for tagged-PDF structure.

**Why it is left:** it needs the default configuration (`/D` with `/ON`, `/OFF`, `/BaseState`, usage and visibility expressions) read and a visibility
stack kept through form XObjects and annotations, in both the painter and the text reader; nothing in the reader's first version asked for layers.

**To close it:** read `/OCProperties` once per document, keep a stack of hidden marked-content spans in `PdfContentInterpreter` and skip painting and
the text sink while one is open, honor `/OC` on XObjects and annotations. Delete this file and the sentence about layers in `docs/peachdrawing-pdf.md` when done.
