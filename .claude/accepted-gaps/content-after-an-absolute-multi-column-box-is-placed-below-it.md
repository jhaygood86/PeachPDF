# Content after an absolute multi-column box is placed below it

_CSS 2.1 §9.3.1 (an absolutely positioned box has no impact on later siblings). Tracker:
[#1377](https://github.com/jhaygood86/PeachPDF/issues/1377)._

An absolutely positioned box is laid out unbroken (`CssBox.LayoutBlockChildUnbroken`), so its break never
ends the layout pass and the content after it is placed at its parent's top, where §9.3.1 puts it. One that
is or holds a multi-column container cannot be: the columns engine records each column for one page slot and
needs the attached fragmentainer, and laid out unbroken it lost its last column lines (see
[the tall absolute box gap](a-tall-absolutely-positioned-box-is-sliced-not-fragmented.md) and #1376). It keeps
the breaking path, and a break inside it ends the pass.

The content after it was then placed at its parent's top, on the page the pass had already emitted, and drawn
on no page. Measured on a 300×200pt page, `<p>B1</p><div><abs columns:2 top:120pt>W1–W8</abs><p>AFTER</p></div>`
lost `AFTER`, and a ten-paragraph following block lost all ten (a review of #1334). So
`DomUtils.IsSteppedOverAsPreviousSibling` does not step over such a box: the content after it is placed below
it, as `main` did for a first child before #1349, and is drawn on the page the pass continues on.

Fixing the position needs the columns engine to run a column on across pages while laid out unbroken (the
cause shared with #1376), or the emitter to re-open a page for in-flow content placed behind the pass.
