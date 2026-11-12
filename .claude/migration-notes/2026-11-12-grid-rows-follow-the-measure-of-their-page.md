# A grid container and its rows follow the width of the page they land on

**Before.** A block-level grid that continued across pages of different widths (per-page margins or sizes) kept the width of the page it started on: its frame and
its `1fr`, `auto` and percentage columns stayed at that measure on every later page.

**Now.** The container's frame, and each row's columns, gaps and items, take the width of the page the row lands on. Fixed-length columns and a grid on a single page are
unchanged; a row whose items continue onto a page of another width keeps the column sizes it started with.

Closes #1516.
