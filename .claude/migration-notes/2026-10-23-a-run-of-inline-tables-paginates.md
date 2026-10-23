# A long run of inline-tables, inline-grids or inline-flex boxes with no text between them now paginates

Before (v0.9.23): when such a run (for example a thousand `display: inline-table` tables separated only by spaces)
crossed the foot of a page, layout never finished; with a word between the boxes it did. Now the run flows onto the next
page like any other inline content. Only where a break between two such boxes resumes changed, so a document that already
finished should lay out as before.
