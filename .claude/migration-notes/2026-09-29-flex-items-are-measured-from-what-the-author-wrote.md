# A flex item is measured from what the author wrote, not from a previous layout's answer, and a flex container follows each page's width

Two visible changes:

1. **Per-page width (#196).** In a document whose pages have different content widths (`@page :first { margin-left: 0 }`,
   per-page `size`), a block-level flex container used to keep the width of the page it started on, on every page
   it continued onto, and its items kept the widths they started with. Now the container's frame is the width of
   each page it is on, a wrapping row's later lines are collected against the page they land on, and a line whose
   items straddle a page break re-fits them to the page they continue onto.
2. **Stale item sizes.** `ItemContentCommit.CommitLayout` pins a flex/grid item's `width`/`height` for good; a
   second measurement of the same item (a flex container inside a table cell is measured several times) read
   that pin back as if the author had written it. Flex items are now measured from their authored `width`/`height`
   each time. Documents where a flex container sits inside a table cell can size its items differently: they are
   narrower where the earlier answer was a first, provisional, wider measurement (`flexbox` showcase, §8).

Neither changes a document with a single content width and no flex container measured more than once.

3. **Blocks inside a multi-column container, in a document whose pages have different content widths.** A multi-column
   container used to count as a link of the "unconstrained main column" chain, so a block-level child of it (a flex
   container, most visibly) measured against the page area the container spans instead of against its column, and a flex
   container came out as wide as the whole container in each column. Children of a multi-column container now measure
   against their column.

