# Tagged declarative documents with several Page handlers keep all their structure

A tagged declarative document (`EnableTaggedPdf`) built with more than one `doc.Page(...)` call used to keep only the last handler's structure in the
structure tree, and the pages of different handlers could share `/StructParents` keys. The structure tree now holds one `Document` element with
the structure of every handler, and every page has its own parent-tree key. Documents with a single `Page` call, and HTML documents, are unchanged.
