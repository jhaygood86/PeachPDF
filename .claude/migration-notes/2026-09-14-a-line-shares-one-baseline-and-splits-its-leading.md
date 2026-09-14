# A line's boxes now share one baseline, and a `line-height` splits its leading

## What changed

Three related things, all of them CSS 2.1
[§10.8.1](https://www.w3.org/TR/CSS21/visudet.html#line-height):

**Text of different sizes on one line now shares a baseline.** It used to share a *top edge*. In
`text <span style="font-size:24pt">BIG</span> text`, the small text was previously level with the top
of the large text; it now rests on the same baseline, as it does in a browser.

**A `line-height` larger than the font now centres the text in the line** instead of hanging it from
the top. Half the leading goes above the content area and half below, so a `line-height: 3` paragraph
is vertically centred in each of its bands rather than sitting at the top of them.

**A line box can now be taller than the largest `line-height` on it.** Its two sides are maximised
independently, because the box reaching highest above the baseline need not be the one reaching
lowest below it.

A document whose fonts are all one size on each line, at the default `line-height: normal`, renders
**identically** — `normal` resolves from the same font metrics the content area is built from, so its
leading is ~0 and nothing moves.

## Two consequences worth expecting

**An `outside` `::marker` sits on its item's first baseline** rather than at the item's top edge, and
that first line grows to hold a taller marker. A `li::marker { font-size: … }` override used to leave
the marker's digits hanging below the text they numbered and left the line's height unchanged; both
now match browsers. (An item whose content is *block-level* — `<li><p>…</p></li>` — keeps the older
placement; the two are indistinguishable unless the marker's font differs from the item's.)

**A line whose box does not fit a page now moves whole, where it used to be kept if its glyphs
fitted.** Break decisions are made against the word's real position, which now includes the
half-leading, so a tall `line-height` over a small font no longer keeps a line on a page its line box
overflows. Expect at most one fewer line on such a page, and the same content on the next one.

## What deliberately did not change

- **Replaced and atomic inline content** — an `<img>`, an inline `<svg>`, MathML, a form control, an
  `inline-block` — still sits at the line's top rather than with its bottom margin edge on the
  baseline.
- **A `line-height` shorter than the font** overflows its line box downwards only. The spec (and
  Chrome) let the glyphs overflow above it as well; PeachPDF holds the line's topmost ink at its own
  top edge, because the page a word is drawn on is decided from the word's own box. The line's
  *height* is the declared `line-height` either way, so line stacking is unaffected.
