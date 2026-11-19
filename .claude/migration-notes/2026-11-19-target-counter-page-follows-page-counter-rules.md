# `target-counter(<link>, page)` honors `@page` counter rules

Before: it numbered by position (page index + 1 + offset) and ignored `counter-reset`/`counter-set`/`counter-increment` on the `page` counter in `@page`.
Now: it shows the same value `counter(page)` shows on the target's page, so a table of contents in a document that restarts page numbering (for example roman
front matter, then `counter-reset: page 1`) has correct numbers. Documents without such rules render as before.
