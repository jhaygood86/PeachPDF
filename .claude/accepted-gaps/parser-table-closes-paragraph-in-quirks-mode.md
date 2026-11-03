# A `<table>` start tag closes an open `<p>` in quirks mode too

The HTML Standard closes a `p` in button scope on a `<table>` start tag only when the document is not in quirks mode. `HtmlParser` closes it always, because the
engine does not determine quirks mode (a `DOCTYPE` token is ignored), so `<p><table>` in a quirks-mode document nests the table in the paragraph in a browser and
not here. Closing is what a standards-mode document (and Acid2) needs, and a document with no doctype is rare in what this library renders. Closing the gap
needs the DOCTYPE quirks algorithm and a flag from the document to the tree builder. Tracked in issue 2141.
