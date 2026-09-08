# Table cells no longer clip their own overflow

The default stylesheet set `overflow: hidden` on `td, th`. The HTML Standard's own rendering section
([§15.3.8, Tables](https://html.spec.whatwg.org/multipage/rendering.html#tables-2)) sets only
`display: table-cell`, `padding: 1px` and — on `th` — `font-weight: bold`. It does not set
`overflow` at all, and no browser does either.

Content too wide for a cell now overflows and is painted, as it is in a browser.

**When a document author would notice.** Only under `table-layout: fixed`, or wherever else a column
cannot widen. Under automatic table layout the column simply grows to fit its content, so the rule
never bit and nothing changes. With a fixed layout the overflow was silently cut instead — measured
by rasterising a 40pt cell holding a 15-character word and counting ink to the right of it:

| | ink right of the cell |
| --- | --- |
| Chrome 152 | 2021 |
| before | 1315 |
| after | 2542 |

`overflow: hidden` declared by the author on a cell still clips exactly as before.

The `table-layout` row in [docs/html-css-support.md](../../docs/html-css-support.md#tables) is
updated to match: overflowing text in a fixed-layout column wraps, and text that cannot wrap
overflows the column visibly rather than being clipped.
