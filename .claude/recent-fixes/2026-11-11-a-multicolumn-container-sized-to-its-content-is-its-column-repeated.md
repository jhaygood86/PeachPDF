# A multi-column container sized to its content is its column repeated (#2340)

**Symptom.** A shrink-to-fit multi-column container (a float, a flex item, a table cell) was as wide as *one* column
of its content, so `float:left; columns:2; column-gap:10px` over two 100px blocks was 100px (230px, the uncapped `width`, before #2330
bounded the intrinsic contribution) and the columns overlapped. Chrome: 210px.

**Load-bearing idea.** `GetMinMaxSumWords` has one branch per box kind whose width is not the flat running line (flex row, grid); a
multi-column container is a third. `TryMeasureMulticolWidth` measures the container's contents once, as one column
(`MeasureContentWidth(columnContent: true)`, which makes the walk skip this branch for the box itself and only for it - nested containers
go through it again), takes its own border and padding back out, and applies:

- max-content = `N * max(W, M) + (N - 1) * gap`
- min-content = `min(m, W)` when a `column-width` is given, else `N * m + (N - 1) * gap`

(`N` the count or 1, `W` the `column-width` or 0, `M`/`m` the column's max-/min-content). css-multicol-1 does not state this - §3.4 leaves the
used width to the sizing specs - so the formulas are the ones headless Chrome gives, fitted against 19 shapes at both sizes (floats for
max-content, floats in a 1px box for min-content) and then matched by PeachPDF in all 38 measurements: count only, width only, both, short
and long content, blocks and text, `<br>`, child padding and `max-width`, container border and padding, default/em/percentage gaps, nested.

**Found by running, not reading.**
- A percentage `column-gap` is 0 here (Chrome 200px for two 100px columns with `20%`), which `ParseLength(gap, 0, box)` gives for free.
- `column-width` alone is not "one column of the content": it is a *floor* on the max-content column (`columns: 80px` over 'ab cd' is 80px)
  and a *cap* on the min-content one (`columns: 80px` over a 288px word is 80px, the word overflows its column).
- `column-count` together with `column-width` still repeats `max(W, M)`, and its min-content is not multiplied (the columns can drop to one).
- PeachPDF has no `width: min-content`/`max-content` keywords (a block with either fills the line), so the min-content half had to be measured
  through a float in a 1px box, in both renderers.

**Found by review, then measured.** A `column-span: all` child is not part of the column: it is left out of the column measure and instead
floors the result (`max(spanner, formula over the rest)`, both sizes); measured with the rest, a 300px heading over 50px columns made a 610px
float (Chrome 300px). With a `column-width`, min-content is never below 1px. `columns` does nothing on an inline box, a replaced element, a
flex or grid container or a table, so `TryMeasureMulticolWidth` returns `false` for those (Chrome ignores it there too). An empty `columns: 3`
float is the two gaps wide, as in Chrome. An unadopted css-multicol-2 proposal (csswg-drafts #4952) agrees with the formulas, apart from forced
column breaks and a constrained height, which Chrome (and this) ignore.

**Not done.** A multi-column container in a vertical `writing-mode` is left as it was (its inline axis is not the width). An `inline-block` with
`columns` and no `width` fills the line instead of shrink-wrapping (789px against Chrome's 210px, on `main` before this change too), and columns
inside a float or inline-block are not balanced (#2341): the container is now wide enough for its columns, but the content still stacks in the
first. The container's available width still caps the result as for any shrink-to-fit box.
