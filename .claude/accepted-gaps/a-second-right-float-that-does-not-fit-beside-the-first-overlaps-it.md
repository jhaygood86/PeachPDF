# A second right float that does not fit beside the first overlaps it

Two `float: right` boxes that together are wider than their container should stack, the second below the first.
Here the second is placed at the same height as the first and the two overlap, so no drop is taken.
`FloatBoxLeft` does drop in the same situation. The right-float branch of `DomUtils.IsFloatIntersecting` is the
place to look: it never reports the earlier right float as colliding for two floats that would occupy the same
rectangle. That is a reading of the code and of the measurements below, not something traced through a debugger.

Measured on a 300pt x 400pt page with 20pt margins, two 200pt-wide, 30pt-tall floats in a 260pt-wide content
area, comparing the vertical distance between the two floats' first lines:

| Floats | Chrome | PeachPDF |
|---|---|---|
| `left`, `left` | 30pt | 30pt |
| `right`, `right` | 30pt | 0pt |
| `right`, `right`, first with `margin-bottom: 10pt` | 40pt | 0pt |
| `right`, `right`, second with `margin-top: 10pt` | 40pt | 10pt |

[css2 9.5.1](https://www.w3.org/TR/CSS21/visuren.html#float-position) rule 8, "A floating box must be placed as
high as possible", and the sentence that a float without enough horizontal room is moved down until it fits,
apply to right floats as they do to left ones.

It shows up more once a tall float continues across pages, because the text that used to be lost beside such an
overlap is now drawn. On a corpus of 500 generated float layouts the number of overlapping word pairs went from
1,542 to 4,873 with the continuation change; the two worst documents were an `overflow: hidden` wrapper holding
only a tall right float next to another right float.

Tracking issue: not yet filed. This note was recorded offline; file the issue upstream with the table above and
add its number here.
