# An empty inline with a larger font now makes its line taller

**Before.** A line box was sized only from the words on it. An inline element with no text of its own,
such as an icon wrapper, `<a name="…"></a>`, or a `<span style="position: relative">` holding only an
absolutely positioned badge, added nothing to its line's height, whatever its `font-size` or
`line-height`. `text<span style="font-size: 40pt"></span>more` at 10pt, `line-height: 1.2`, gave a 12pt
line. A badge anchored to such a wrapper hung from a 40pt-tall box extending above the 10pt line.

**Now.** An empty inline counts toward its line's height, the same as an inline holding text: that
example gives a 48pt line, the text sits lower on the shared baseline, and the badge's box lies inside
the line. An empty inline right after a space goes to the next line with the word after it when that
word wraps, and one before a `<br>` stays on the line the break ends. A line holding nothing but empty
inlines stays zero-height. Documents whose empty inlines use the surrounding font size are unaffected;
none of the 163 showcases changed apart from the one extended to show this.

**Why.** CSS 2.1 §9.4.2 generates an inline box for an empty inline element, and §10.8.1 makes the line
box tall enough for every inline box on it. Confirmed against `v0.9.19`: `FlowBox` grew a line only per
placed word there (`GrowLineToItsExtent`), and nothing counted an inline that placed none. Vertical
writing modes still do not count it.
