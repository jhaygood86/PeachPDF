# `overflow: clip` and `overflow-x: hidden` do not clip a box's content

A box with `overflow: clip` or `overflow-x: hidden` does not clip its overflowing content: its text runs
past the box edge, where a browser clips it. `overflow: hidden` does clip. Measured through the CLI on an
80px `inline-block` with `white-space: nowrap`, box at x = 24..104: text reaches 225 for `clip` and for
`overflow-x: hidden`, 102 for `hidden`.

`DomUtils.ClipsItsOverflow` only recognises `Overflow.Hidden` on the single `overflow` value. Whether
`clip` and the per-axis longhands are parsed but never mapped to a clip, or are stored differently, was not
traced.

**Why it matters for text decoration:** a decorated `inline-block` used to have its underline confined to the
box's own rectangle. Since the #1320 fix it follows the box's inline content (css-text-decor-3 section 2),
so with unclipped text the underline now runs out to the end of the text for these two values (x = 226
above), where it used to stop at the box edge. The underline agrees with the text beside it; the text is
what is not clipped. `overflow: hidden` clips both (`PropagatedUnderline_OfAClippingBox_IsDrawnInsideItsOwnClip`).

Documented in the `overflow` and `text-decoration-line` rows of `docs/html-css-support.md`. Tracking issue:
[#1628](https://github.com/jhaygood86/PeachPDF/issues/1628). Closing it means deleting this file and the two
doc sentences.
