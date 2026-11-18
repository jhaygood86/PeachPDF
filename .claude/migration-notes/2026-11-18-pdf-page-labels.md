# PDF page labels are written, and merged PDFs keep theirs

New: `PdfComposer.AddPageLabels`, `IPageDescriptor.PageLabel` and `IDocumentBuilder.PageLabels` write the PDF's page labels (what a viewer shows in its page box and thumbnails).

Changed: `PdfOperation` (take, merge, append) now carries the page labels of the pages it takes. Before, a merged or extracted PDF lost its labels and the viewer showed plain numbers; now a file with labels keeps them (a new range starts wherever neighboring labels do not follow on). Files without labels are unchanged.
