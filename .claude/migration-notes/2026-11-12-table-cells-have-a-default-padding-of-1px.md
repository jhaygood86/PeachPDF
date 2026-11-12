# Table cells have a default padding of 1px

**Before.** A `td` or `th` with no `cellpadding` on its table and no `padding` of its own had no padding, so unstyled tables were 1px (0.75pt) tighter on every side of every cell than in a browser.

**Now.** The cell has the browser's default `padding: 1px`. A table that sets `cellpadding` (including `0`) or a cell or stylesheet that sets `padding` is unchanged. A document that relied on the unpadded look (a table with no `cellpadding` and cells sized to the pixel) gets cells 2px wider and taller; add `cellpadding="0"` or `td, th { padding: 0 }` to restore it.

**Also.** A cell that holds a table is now as wide as that table including its `border-spacing` (it was narrower by two spacings, and the table painted outside it), which shifts a table nested in an auto-width cell or in a flex item.

**Checked.** `git show v0.9.26:src/engine/PeachBrackets/Html/Core/CssDefaults.cs` has no padding rule for `td, th`, so this differs from the last release.
