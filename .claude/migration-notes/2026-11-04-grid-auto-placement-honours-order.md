# Grid auto-placement honours `order`

**Before.** `order` was applied to flex items only. Grid items were auto-placed in source order, so a layout that writes its sidebar first and gives it `order: 2`
(content `order: 1`) put the sidebar in the first column and pushed the content into the next ones (arstechnica.com's front page: the article list sat in the
right two thirds of the grid with the first column empty).

**Now.** Grid auto-placement takes the items in order-modified document order (CSS Grid 1 section 8): a lower `order` is placed first, equal orders keep source order.
Items placed explicitly (`grid-row`/`grid-column` lines) are unaffected. Painting order for grid items is still source order.

Not verified against `v0.9.26-preview1`; the reference page listed `order` as fully supported without saying it was flex-only.
