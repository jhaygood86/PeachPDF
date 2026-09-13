# A float beside inline content now widens the shrink-to-fit box that holds it

Issue #1033.

A shrink-to-fit box (a float, a `position: absolute` box with `auto` width, an auto table column)
holding both inline content and a float used to be sized to the **wider** of the two. It is now sized
to their **sum**, because a float is placed beside the inline content of the block it is in (CSS 2.1
§9.5) rather than on a line of its own.

```html
<div style="float: left; font: 16px monospace">XY <span style="float: left">ZZZZ</span></div>
```

was 26.3906pt wide — the float alone — and is now 39.5859pt: `XY` plus `ZZZZ`. The space between them
is not counted, because a float is out of flow and css-text-3 §1.5 ignores out-of-flow elements when
deciding adjacency, so that space is still at the end of the line's own in-flow content and hangs
(§4.1.2). The same applies to two floats with nothing between them: `AAA` + `BBB` used to size the
container to one of them, so the second wrapped underneath; both now fit side by side.

Documents that relied on the old, narrower size will see such a box grow, and content that used to be
pushed onto a second line inside it may now fit on one. Boxes with no float among their inline content
are unaffected.

Two floats that each declare their own `padding`/`border` still lose one of the two from the line they
share, so such a box can measure short — see
[Floats](https://peachpdf.net/html-css-support.html) in the HTML/CSS support matrix.
