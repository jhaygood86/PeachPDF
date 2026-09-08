# A `colspan` constrains its columns together, not each one individually

`GetColumnsMinMaxWidthByContent` divided a spanning cell's min/max width by its span and
`Math.Max`'d the result into every column it covered. CSS 2.1 §17.5.2.2 step 3 constrains the spanned
columns *together* — "so that together, they are at least as wide as the cell" — and says nothing
about any one of them. The difference only shows when the spanned columns are uneven: dividing drags
the narrow one up to the average, and the surplus pushes every later boundary out.

Spanning cells are now deferred to a second pass, so every single-column cell has already been
applied and the sums a span is measured against are final. `SpreadSpannedWidth` then adds only the
shortfall, if there is one.

## What running it turned up

- **The obvious repro does not discriminate.** A four-column header with two `colspan="2"` detail
  rows — the shape this was originally found on — gives identical output before and after, because
  the span genuinely needs widening there and both rules widen it. The fixture that separates them
  is an *uneven* spanned pair whose sum already fits: a one-character column beside a long one.
- **Chrome agrees, and the measurement is now in the doc comment.** Rendering that fixture through
  Chrome 152 headless and reading positions with `pdftotext -bbox`, the second column boundary is at
  ~7.0pt and the third at 197.3pt past the page margin; this change gives 7.2 and 195.1, the old rule
  23.3 and 211.2. The residual ~2pt is border/padding modelling, present in both rows.
- **The proportional split is an approximation, and is now labelled as one.** §17.5.2.2 says to widen
  the spanned columns by "approximately the same amount". Sharing the shortfall in proportion to what
  each column already measures avoids re-inflating a column already sized by its own content, and no
  browser implements §17.5.2.2's automatic algorithm literally — but it is a choice, not the spec
  text, and the two differ only in how a genuine shortfall is split.
- **Overlapping spans are order-dependent**, because spans apply in document order and each mutates
  the widths the next measures against. The invariant that survives is §17.5.2.2's own requirement as
  a postcondition — every spanning cell's columns together hold that cell — which growth-only updates
  reach in any order. Pinned by a fixture with a `colspan="3"` over columns 0-2 and a `colspan="2"`
  over columns 1-2.

## Evidence

`TableColspanColumnSizingTests`, four fixtures, each measured against a control table differing only
in the row under test: a span that already fits (no boundary moves), a span that does not (they
move), the proportional ordering, and the overlapping-span postcondition. Two fail against the merge
base. Full suite green on net8.0, 0 build warnings. The collapsed-column exclusion
(`IsColumnCollapsed`) is carried into the new pass unchanged.
