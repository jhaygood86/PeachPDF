# `text-align: justify` expands at every word boundary, not at justification opportunities

Tracked as **#1013**. Found during the issue #1011 review; pre-existing, not introduced by it.

`CssLayoutEngine.ApplyJustifyAlignment` (and its vertical counterpart in the column path) sums every
word's own `Width` on a justified line and spreads `(availWidth - textSum) / wordCount` after **each**
word, consulting `HasSpaceAfter` only as a floor for an overflowing line. Per
[css-text-3 §7.3](https://www.w3.org/TR/css-text-3/#justification) expansion belongs at
*justification opportunities* — for `text-justify: auto` in a Latin script, the word separators — not
at every inline-box boundary.

Measured at `font: 16px monospace`, `width: 200pt`, on a genuinely justified (non-final) line:
`A<span>B</span> CD EF …` places `B` 6.186pt after `A`, where left alignment places it flush at
`A`'s right edge and a browser renders `AB` contiguous. The same gap opens on both sides of an
`<img>`/inline `<svg>` that has no source white space around it.

Out of scope for issue #1011, which was about the *natural* inter-word gap
(`CssRect.ActualWordSpacing`): that term is shared by every alignment and by intrinsic-width
measurement, while this one lives entirely in the justify pass and needs a real
justification-opportunity model (word separators, and the `text-justify` property PeachPDF does not
implement) rather than a word count. Note that `CssRect.ActualWordSpacing`'s own remarks now state the
"only source white space produces an advance" rule, and this is the one place that rule does not hold.
