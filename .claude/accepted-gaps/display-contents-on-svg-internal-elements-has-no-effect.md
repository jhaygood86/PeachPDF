# `display: contents` on SVG-internal elements has no effect

[CSS Display 3 Appendix B](https://www.w3.org/TR/css-display-3/#unbox) says `display: contents` on an SVG
container that is also a renderable element (`g`, `a`, `switch`, …), on SVG text content child elements
(`tspan`, `textPath`, …) and on `use` strips the element from the formatting tree and hoists its contents
into its place; every other SVG element computes to `display: none`.

**Why it was left.** The HTML implementation splices boxes out of the `CssBox` tree
(`DomParser.FlattenDisplayContents`). The SVG renderer is a separate tree (`SvgTreeBuilder` →
`SvgElement`) and it never reads the CSS `display` property at all — not `none`, not `contents`. Adding
`contents` there means adding `display` handling to the SVG tree builder first, which is the larger gap and
a change to a different subsystem.

The cascade does still visit an inline `<svg>`/`<math>` subtree, so those boxes are deliberately not recorded as
shells (`CascadeApplyStyles` passes no shell list below a `CssBoxSvg`/`CssBoxMath`): lifting a `<g>` out of the
tree the SVG builder reads would drop its `transform`/`opacity`/`clip-path` and break a `<use>` pointing at it.
MathML internals are treated the same way.

**What happens instead.** A `<g style="display: contents">` renders as an ordinary group, so its own
presentation attributes (`transform`, `opacity`, `clip-path`, …) still apply to its children, where the
spec says they are ignored once the element is stripped from the formatting tree. An *inline `<svg>`* with
`display: contents` is handled by the HTML side: it computes to `none`, as Appendix B requires.

Tracked as [issue #1295](https://github.com/jhaygood86/PeachPDF/issues/1295).
