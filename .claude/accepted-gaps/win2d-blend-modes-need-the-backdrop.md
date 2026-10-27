# Win2D: only add, minimum and the layer-versus-layer blends are exact

`Win2dCanvas.PushBlendMode` (and a `BeginLayer` with a blend mode) maps `Plus` to Win2D's `Add` and `Darken` to `Min`, which a drawing
session applies while it draws. Every other `PaintBlendMode` (`Multiply`, `Screen`, `Overlay`, `Difference`, the non-separable ones) is
drawn as `Normal`. `Lighten` also falls back: Win2D's `CanvasBlend` has no maximum.

**Why.** A Win2D session blends source against destination only by those few fixed rules. A general blend needs the pixels already
underneath as an input (`BlendEffect`), and a drawing session cannot read its own target while it is drawing to it (Direct2D forbids using
the render target as an image source). Reading it would mean closing the session, copying the target, and re-opening it for every blended
draw, which is both slow and incompatible with a session the caller owns (a swap chain or a `CanvasControl`).

**What is exact.** `DrawImageBlendedOver` (SVG `feBlend`) blends two tiles, both in hand, with the full `BlendEffect` set. A group opacity
or colour effect is exact. CSS `mix-blend-mode` and `outline-color: invert` (`Difference`) on a Win2D canvas are the cases that are not.

**If revisited:** an offscreen composer owns its bitmap, so it could snapshot between blended draws; a caller's session could not. Do that
only for the composer, and only with measurements.
