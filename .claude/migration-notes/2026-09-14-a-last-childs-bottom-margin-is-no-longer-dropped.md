# A last child's bottom margin is no longer dropped

A block's block-end margin and its last in-flow child's are one adjoining set whenever nothing of the
block's own separates them (CSS 2.1
[§8.3.1](https://www.w3.org/TR/CSS21/box.html#collapsing-margins)). PeachPDF used to lose that child
margin entirely for any block that had a following sibling — it neither escaped into the gap after the
block nor stayed inside the block's height.

```html
<div class="wrapper"><p style="margin-bottom: 12pt">…</p></div>
<div class="next">…</div>
```

`.next` used to start immediately at `.wrapper`'s bottom edge. It now starts 12pt below it, because
`<p>`'s margin collapses through the wrapper and lands in the gap — which is what every browser does.

Three consequences a document author can see:

- **A gap that was missing now appears.** Wherever a bottom margin sat on the last child of a wrapper,
  the space it asks for is now honoured. Documents that leaned on the old behaviour will get taller,
  and may repaginate.
- **A negative `margin-top` on the following element now has something to collapse against.** It
  previously applied in full against a wrapper whose margin read as zero, pulling content up too far.
- **When collapsing is blocked, the child's margin is now counted inside the block** instead of
  disappearing. A wrapper with a `border-bottom`, `padding-bottom`, or `overflow: hidden` grows by its
  last child's bottom margin — for `overflow: hidden` around a 10pt child with a 50pt bottom margin,
  from 10pt to 60pt. A negative child margin correspondingly *shrinks* such a wrapper.

One related case is unchanged and still wrong: a self-collapsing last child is positioned below its own
collapsed-away top margin, which inflates its parent. That is a defect in self-collapsing box placement
rather than in margin collapsing, and it is untouched here.
