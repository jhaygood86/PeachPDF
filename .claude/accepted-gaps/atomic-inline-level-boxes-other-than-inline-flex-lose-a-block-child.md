# Only `inline-flex` is read as the atomic inline it is; the other three lose a block-level child

_Tracked as [#473](https://github.com/jhaygood86/PeachPDF/issues/473). Left out of scope by
[#462](https://github.com/jhaygood86/PeachPDF/issues/462), which fixed the `inline-flex` case._

`DomParser.CorrectBlockInsideInline` uses `ContainsInlinesOnlyDeep`, which descends into every box
with `IsInline == true`. An **atomic** inline-level box is not an ordinary inline box — its contents
establish an independent formatting context ([CSS Display 3 §2.3](https://www.w3.org/TR/css-display-3/#atomic-inline))
— so descending into one and splitting it hoists its first block-level child *out of the box*, to be
laid out as a sibling. The child is then drawn outside the box's own border and the box reports only
what is left.

`DomParser.IsAtomicInlineLevel` names `inline-flex` and nothing else, so `inline-block`,
`inline-grid` and `inline-table` still have it.

**Why the obvious one-line widening is wrong, measured.** Adding the other three to that predicate
fixes nothing and breaks three things. `CssLayoutEngine.FlowBox` has an atomic branch for
`inline-flex` alone; the others fall into the recursion that walks a box's children as the parent
line's own inline content, which places *nothing at all* for a block-level child. Measured on
`<div style="display:X; width:200pt">` holding a 10pt and a 40pt `<div>`:

| display | with the split | with the split suppressed |
|---|---|---|
| `inline-flex` | first item hoisted out, container 40pt | correct: both inside, container 50pt |
| `inline-block` / `inline-grid` / `inline-table` | first item hoisted out | nothing laid out at all — container `height: 0` at the origin |

So closing this is a change to *inline layout* (an atomic placement branch for the other three), not
to the box-tree fixup, and it belongs with the two `inline-block`/`inline-table` atomicity limits
already recorded under "Atomic inline-level layout is approximated, not fully atomic" in
`docs/html-css-support.md`.
