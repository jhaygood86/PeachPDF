# Auto table columns no longer overflow a `width`-constrained table

## What changed

**A table with a definite width (e.g. `width: 100%`, or an explicit `pt`/`px` width) and several
columns with no per-column width could render wider than the width it was actually given**, up to the
point of overflowing its page. This happened whenever the table had multiple auto (no explicit width)
columns whose combined un-wrapped (max-content) width didn't all fit, but whose combined *minimum*
content width did: some columns would keep their entire content on one un-wrapped line while sibling
columns were squeezed down toward their bare-minimum wrapped width, with the sum of every column never
checked against the table's own available width. Which columns kept their full width and which were
squeezed was effectively arbitrary — an artifact of column processing order and exact text-measurement
widths, not any property of the content itself.

```html
<table style="width: 300pt; border-collapse: collapse">
  <tr>
    <th>C (Carbon)</th><th>Mn (Manganese)</th><th>Ni (Nickel)</th><th>Ferrite Content</th>
  </tr>
</table>
```

Before this change, a table like this could render some headers on a single un-wrapped line while
others wrapped, and the table's own rendered width could exceed its declared `300pt` — in a document
with several such tables stacked tightly against a page edge, this could push the last column's content
past the page boundary, clipping it.

**Behavior now follows [CSS 2.1 §17.5.2.2](https://www.w3.org/TR/CSS21/tables.html#width-layout) more
closely: every auto column's used width is resolved between its own min-content and max-content width**,
using the width actually available to the table's auto columns as the budget. When that budget falls
short of every column keeping its full un-wrapped width, every auto column gives up width
proportionally, landing between its own minimum and maximum rather than some columns keeping full width
while others are squeezed to bare minimum. The table's rendered width no longer exceeds its declared
width in this case (except when even every column's own content minimum doesn't fit — CSS 2.1 never
shrinks a column below its content minimum, so that overflow is unavoidable and unchanged).

Rendered output for such tables may look different: columns whose header/cell text previously stayed on
one line may now wrap across two or more lines, and the table's overall width may be narrower than
before (and, in the specific overflow case this fixes, no longer wider than its container). A table
whose auto columns already had enough combined room for every column's full un-wrapped width is
unaffected — this only changes tables where that room was genuinely short.
