# Monolithic content taller than a page now overflows instead of fragmenting

Before: a scroll container (`overflow` other than `visible`) whose content was taller than every page's
own content band kept fragmenting internally — its content took ordinary page breaks, including honoring
`break-before`/`break-after`/`break-inside` on its descendants, exactly like a non-monolithic block.

Now: per CSS Fragmentation Level 3 §2, such a box's content lays out as one continuous, unbroken run and
simply spans as many pages as it needs, each page showing its own slice — matching how any other
oversized content (a large image, an ordinary tall block) already renders. A forced break
(`break-before`/`break-after: page`, etc.) on a descendant inside such a container **no longer takes
effect** — no page break is manufactured for it — since honoring one would itself be a form of splitting
the monolithic content that §2 forbids.

Verified against `main` at the previous tag: `docs/html-css-support.md`'s monolithic-content section
previously stated "Content that fits in no page at all ... keeps fragmenting rather than overflowing" as a
deliberate, documented limitation; that sentence is removed as part of this change.

Table cells (`<td>`/`<th>`) are unaffected — despite getting `overflow: hidden` from the UA stylesheet,
their own fragmentation across pages continues to be governed entirely by the table layout engine, not by
this rule.
