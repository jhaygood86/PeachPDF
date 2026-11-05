# The annotation collector threw for a list that runs over the columns

**Symptom.** `PeachPDF.TestHarness` crashed on the `marker_styling` showcase (and so would the docs site's showcase build): `ArgumentException: An item with the same key has
already been added. Key: <ul#248>`, from `AnnotationPropertyCollector.Walk` via `HtmlDocument.Annotations`, which `PdfGenerator.RenderPagesCore` reads for every document. Any
document with a list (or any box) whose content runs past the end of a column, with the box having a fragment in each column, hit it.

**Cause.** The walk indexed a fragment's children by box with `ToDictionary(child => child.Box)` (to place an empty annotated inline next to its neighbours). A box that continues into
the next column has a fragment in each column under the same parent fragment, so the same box was a key twice. Added with #2193 (annotation properties); nothing caught it because no
test lays out a column-split box and asks for annotations.

**Fix.** The index keeps the first fragment of a box (`TryAdd`), which is where it begins. Test: `HtmlDocumentTests.AListThatRunsOverTheColumns_HasAnnotationsAndDoesNotThrow`. Found by running
the whole showcase set (240 pages) to compare two engine builds: it stopped after 19.
