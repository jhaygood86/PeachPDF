# A float that does not fit its column moves to the next column

A block-level float in a multi-column container whose margin box did not fit below the content already in its column used to hang below the
column (and grow the container). It now starts the next column; the content after it stays where it was.
