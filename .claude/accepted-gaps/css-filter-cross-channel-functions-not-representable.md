# Cross-channel `filter`/`feColorMatrix`/`feComponentTransfer` functions have no PDF mechanism

CSS `filter: grayscale()`, `sepia()`, `saturate()`, `hue-rotate()`, and SVG `feColorMatrix
type="saturate"`/`type="hueRotate"` (plus any `type="matrix"` whose 20-value matrix has a nonzero
off-diagonal term) and `feComponentTransfer type="gamma"` share one root cause and are one gap, not
several: each mixes color channels together — every output R/G/B channel is a function of more than one
input channel — and no PDF construct can retroactively perform that kind of mixing over content that has
already been painted/composited, without rasterizing it to a bitmap first (which PeachPDF's vector-first
architecture deliberately never does — see `RGraphics.CreateTile`'s contract).

This was confirmed, not assumed, during implementation of the sibling native-`filter`/native-`<filter>`
work (see `src/PeachPDF/Html/Adapters/Entities/ColorMatrix.cs`'s own type-level remarks for the full
derivation): PDF `ExtGState /TR` (transfer function, ISO 32000-1 §8.6.5.3) is explicit that it is a
*per-component*, not per-color, operation — "a transfer function ... adjusts the values of a single
colour component," applied to "all process colorants" independently, with no way for the function
computing one channel's output to see another channel's input value. `brightness()`, `contrast()`,
`invert()`, and `feComponentTransfer type="linear"` are all genuinely diagonal (each output channel is a
function only of that same input channel) and map exactly onto `/TR` — see
[HTML & CSS Support](../../docs/html-css-support.md#filters-and-blend-modes) and
[Filters](../../docs/supported-svg-features.md#filters) for what *is* supported this way. The
non-diagonal functions above do not, full stop — not "awkwardly," not "with a workaround."

A PDF `DeviceN`/`Separation` colour space's tint-transform function (ISO 32000-1 §8.6.6.2, §7.10) *can*
do arbitrary cross-channel mixing, but only as a way of *specifying what a colour value or an image's raw
sample data means* — it is not a mechanism for *re-processing something already painted*. Retrofitting
it onto arbitrary already-composited vector content (paths, text, gradients, patterns already burned
into a Form XObject's content stream) would mean either rewriting every paint operator in the affected
subtree to go through a shared `DeviceN` colour space up front (an invasive, whole-subtree change, not a
compositing-time one) or rasterizing the tile and applying the matrix per pixel in software — exactly
what browsers do when printing a CSS-`filter`ed, non-channel-independent subtree to PDF, and exactly what
this codebase's vector-fidelity goal rules out.

These four CSS functions and the listed SVG primitive/type combinations parse successfully (so
`filter: grayscale(1) opacity(0.5)` is a valid declaration, and its `opacity()` still applies) but
contribute no visual effect of their own — the same documented, no-rasterization-fallback status as
`filter: blur()`. No new GitHub issue was filed for this gap: unlike an approximation that could plausibly
be tightened by more engineering effort (e.g. [issue #207](https://github.com/jhaygood86/PeachPDF/issues/207)'s
em/rem `calc()` approximation), this is not a deviation from how PeachPDF chose to implement the CSS/SVG
Filter Effects spec — it is a description of what the target PDF construct can do at all, on the terms
this project's vector-fidelity architecture already accepts (the same terms that make `filter: blur()`
and `feGaussianBlur` permanent no-ops, both already accepted without a tracking issue). There is no
future PeachPDF-side fix that doesn't mean abandoning vector output for the affected content.
