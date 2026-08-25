# `@page` margin-box gradient `content` resolves `em`/`rem` against the document root, not the margin box's own font-size

`MarginBoxRenderer.PaintImage` paints a margin box's `content: <image>` (a `url()` or gradient function,
CSS Paged Media Level 3 §7) through the same `CssImagePainter.Paint` pipeline in-flow `content: url()`
uses - but a margin box has no real, laid-out `CssBox` of its own (this whole renderer is a lightweight
text/image-only pass, not a full layout pass), so it passes `htmlContainer.Root` (the document's root
box) as the `box` parameter instead. The method's own doc comment already flagged this as "unreachable
for the keyword position/'auto' values used here, but a real, already-laid-out box is a safe, defensible
fallback if that ever changes" - issues #823/#824's fix is that "if": `CssImagePainter`'s gradient
stop-position/explicit-radius resolution now actually consults `box.GetEmHeight()`/`GetRemHeight()` for
`em`/`rem`-unit values (previously stop positions used a hard-coded default and explicit radii threw for
any relative unit), so this previously-inert fallback is now live for gradient `content` values.

A margin box with its own `font-size` and an `em`-unit gradient stop/radius in its `content` has that
`em` resolve against the *root element's* font-size, not the margin box's own - e.g. `@top-center {
font-size: 20px; content: radial-gradient(2em at center, red, blue); }` with `html { font-size: 10px; }`
resolves the radius to `20px` (`2 * 10px`, the root's size) instead of the spec-correct `40px`
(`2 * 20px`, the margin box's own declared size).

`MarginBoxRenderer` already resolves the margin box's own font (`BuildFont(marginRule.Style, pageStyle,
adapter)`, used for the box's *text* `content`) independently of any `CssBox` - a real fix likely needs
`CssImagePainter`'s gradient-length resolution to accept an already-resolved font-size directly for this
one caller, bypassing `CssBox.GetEmHeight()`/`GetRemHeight()` entirely, rather than threading a real
`CssBox` through a pipeline that deliberately has none. That's a more surgical, wider-reaching change
than #823/#824's literal scope, and narrow in practice (needs an `em`/`rem`-unit gradient stop or
explicit radius specifically inside `@page` margin-box `content`, with that margin box's own font-size
differing from the root's). Tracked as
[#827](https://github.com/jhaygood86/PeachPDF/issues/827).
