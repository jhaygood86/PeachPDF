# A table moved to the next page is as tall as its content

**Before (v0.9.20):** a table whose cells contained a `break-inside: avoid` block, and that was moved to the next page because
the block did not fit the foot of its page, kept the height of the gap it left behind. Its rows were that much too tall and
the content after it started that far below, which showed as a band of blank space (up to a page's remaining height) after
the table.

**Now:** the table is as tall as its content, and what follows it starts directly after it. A document with such a table
renders shorter and may need fewer pages.
