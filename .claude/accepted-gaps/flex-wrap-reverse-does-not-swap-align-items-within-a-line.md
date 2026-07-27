# `wrap-reverse` swaps the cross axis for the lines, not for the items inside a line

_Tracked as [#459](https://github.com/jhaygood86/PeachPDF/issues/459). Left out of scope by
[#458](https://github.com/jhaygood86/PeachPDF/issues/458), which reversed the line stack._

`flex-wrap: wrap-reverse` swaps a flex container's cross-start and cross-end directions
([CSS Flexbox 1 §5.3](https://www.w3.org/TR/css-flexbox-1/#flex-wrap-property)). `DistributeCrossSpace`
now honours that for the **lines** — they are stacked in the reversed direction rather than having
their offsets permuted. `ComputeCrossOffsets` still places each **item within its line** against the
unswapped edges, so under `wrap-reverse`:

- `align-items`/`align-self: flex-start` puts an item at the top of its line where cross-start is now
  the bottom, and `flex-end` the mirror of that;
- a `stretch`/`normal` item that *cannot* stretch (it has a definite cross size) falls through to the
  same flex-start arm, which is the common case rather than an exotic one;
- `baseline` should align against the last baseline set under a reversed cross axis, and aligns
  against the first.

Measured: a 200pt row container, `wrap-reverse; align-items: flex-start`, first line in flow holding a
10pt and a 40pt item (line cross size 40pt), second line one 40pt item. The first line occupies
`[46, 86]` and the 10pt item sits at `y = 46`, the line's top; §5.3 + §8.3 put it at `y = 76`.

**Why it was not taken with #458.** #458 is about where a *line* sits; this is about where an *item*
sits inside one, in a different method. Swapping the item arms changes the rendering of every
`wrap-reverse` container whose line holds items of unequal cross size — including through the
`stretch` fallback — which wants its own tests and its own showcase evidence rather than riding along
with a line-stacking fix. Note for whoever takes it: the two reversals are separate, and applying the
line one a second time inside a line would cancel it.

`stretch` proper is unaffected: an item filling its line's whole cross size is on both edges at once,
which is why the default case looks right and this stayed invisible.
