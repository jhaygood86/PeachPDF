# Negative leading overflows a line box downwards only, never upwards

Tracked by [#1054](https://github.com/jhaygood86/PeachPDF/issues/1054).

CSS 2.1 [§10.8.1](https://www.w3.org/TR/CSS21/visudet.html#line-height): when a `line-height` is
shorter than the font's own height the leading is negative, and half of it is subtracted from *each*
side — the glyph content area then overflows its line box both above and below. Chrome does exactly
that: a `10pt/5pt` line is 7 CSS px tall with its content area starting 5px **above** the line.

PeachPDF applies the full negative leading to the line's **height** (so `line-height: 5pt` really is a
5pt line, and the next line follows 5pt down) but holds the line's topmost ink at the line box's own
top edge, so the overflow is downwards only. The floor is applied **line-wide**, not per box — see
`CssLayoutEngine.ApplyVerticalAlignment`'s `escape`, and `HalfLeadingOffsetOf`'s `Math.Max(0, …)`
counterpart on the flow path — so every box on the line moves by the same amount and the baseline
they share is preserved exactly; only the whole line's ink shifts down inside its own box.

## Why

This engine decides which fragmentainer a word belongs to from **the word's own rectangle**
(`Fragmentation.FragmentEmitter.ClaimsWord`), not from the line box that owns it. Ink that escapes
above its line box therefore escapes the fragmentainer the line was placed in. At a page or column
boundary the consequences are not cosmetic:

- a line placed exactly at a page's content top has its words claimed by the page **above** it, or —
  their bottoms being past that page's own end — by *neither*, and they vanish entirely;
- a line box is a monolithic break unit
  ([css-break-3 §4.1](https://www.w3.org/TR/css-break-3/#possible-breaks)), so its content has to go
  with it.

This was not hypothetical: `ResumableInlineLayoutIntegrationTests.LineMixingTallAndShortContent_MovesWholeInsteadOfStrandingItsShorterWords`
fails without the floor, with the line's text on page 0 and the image beside it on page 1.

Note that `line-height: 1` is already negative leading for most fonts (Arial's content area is about
1.15em), so this is a common configuration, not an exotic one — which is exactly why the failure mode
had to be closed off rather than accepted.

## What closing it would take

Making **the line box**, not the word, the unit of fragmentainer membership: `ClaimsWord` and the
`box.Words` walk around it would have to resolve a word's slot through the line that owns it. That is
the same ownership problem
[`line-word-and-nested-fragment-geometry-can-diverge-across-font-metrics.md`](line-word-and-nested-fragment-geometry-can-diverge-across-font-metrics.md)
(#1048) describes from the other side, and it should be closed with that one rather than separately.

`BaselineAlignmentLayoutIntegrationTests.ALineHeightShorterThanTheFont_KeepsItsInkInsideItsLineBox`
states the current behaviour, including that the line's own height is still the declared
`line-height`.
