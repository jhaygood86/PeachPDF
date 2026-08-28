# Rounded borders, backgrounds, `overflow: hidden`, `clip-path`, and box-shadow rings under a non-default `PixelsPerInch`

Previously, under a non-default `PdfGenerateConfig.PixelsPerInch` (anything other than the default 72),
several kinds of curved/clipped geometry rendered too large and mis-positioned relative to the rest of the
page, proportionally to `PixelsPerInch / 72`:

- A rounded border's own **stroke** could visibly overshoot past the box's actual content, into whatever
  rendered below or beside it.
- A rounded `background-color`/`background-image` fill, or a `background-clip: padding-box`/`content-box`
  curve, could render offset from the border/content it was meant to fill.
- An `overflow: hidden` box's rounded descendant clip curve could fail to overlap its own (correctly
  positioned) content at all - clipping a clipped child's fill, or occasionally its text, away entirely.
- A `clip-path: polygon()/inset()/circle()/ellipse()` shape could render badly mis-scaled and mis-positioned
  - e.g. a `circle(50%)` clip rendering as a mangled quarter-shape instead of a full circle.
- A blurred `box-shadow: inset ...`'s concentric ring fills (its falloff approximation) could render
  detached from the box they were meant to shade, rather than framing it symmetrically.

All of the above are now correct: rendering at any `PixelsPerInch` produces the same position and size as
at the (unaffected) default `PixelsPerInch` of 72. Documents that only ever used the default `PixelsPerInch`
saw no change.

Two related things are **not** part of this fix and still scale incorrectly under a non-default
`PixelsPerInch`:

- A rounded border's stroke **width** (its thickness, as opposed to its position) - tracked as
  [issue #851](https://github.com/jhaygood86/PeachPDF/issues/851).
- A blurred `box-shadow`'s number of concentric approximation layers (a rendering-smoothness difference,
  not a wrong position) - tracked as [issue #852](https://github.com/jhaygood86/PeachPDF/issues/852).
