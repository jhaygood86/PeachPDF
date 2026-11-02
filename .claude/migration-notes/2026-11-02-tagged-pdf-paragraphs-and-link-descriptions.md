# Tagged PDF tags text blocks as paragraphs and describes every link, figure and list

**Before:** with `EnableTaggedPdf`, a block that held only text (`<footer>`, a bare `<div>`, the text between two blocks) was a `Div` or `Span` with the text loose inside it; a link annotation had no `/Contents`; a `<figure>` wrapping a picture and its caption was a `Figure` with no `/Alt`; a list had no `ListNumbering`; an underline was drawn outside any marked content; and an annotation's parent-tree entry was an array.

**Now:** a block holding only inline content is a `P`. Every link annotation has `/Contents` (the link's accessible name, or its address when it has none), a `<figure>` takes its name from its caption as `/Alt`, an `L` has a `List` attribute with `ListNumbering` (`Disc`, `Decimal`, `LowerRoman`...), a text decoration line is an artifact, and an annotation's parent-tree entry is its structure element directly, which readers follow from the link to the `Link` element. In a PDF 2.0 file the structure elements name the PDF 2.0 namespace, and links and outline entries that lead within the document also lead to a structure element. Rendered pages are unchanged.

**Why:** these are the findings of validating generated files with veraPDF; after them a page of headings, navigation, a figure, lists, links and a table passes the PDF/UA-1 and PDF/UA-2 profiles.
