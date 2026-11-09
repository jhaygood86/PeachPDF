# Text after a float that continues in the next column is no longer lost

In a multi-column container, a paragraph that follows a float which does not fit the first column and continues in
the next one (or on the next page) used to be dropped, or its last line drawn twice. It is now placed at the top of the
column where the float continues, beside or below it, as a browser does. A document that lost such words now has them;
the page count of such a document can grow by the page the text needed.
