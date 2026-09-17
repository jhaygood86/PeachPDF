# A `display: block` replaced element with `margin: auto` and no declared width does not center correctly

`ResolveAutoHorizontalMargin`'s `IsReplacedBlockWrapper` branch (`CssLayoutEngine.cs`) resolves the
element's used width by reading its declared CSS `width` directly, since this engine's synthetic
wrapper for a `display: block` `<img>`/`<svg>` (`CssBox.IsReplacedBlockWrapper`) never goes through
ordinary block-width resolution. When no declared width exists — the element relies on its intrinsic
pixel size instead — the code falls back to `box.FirstWord.Width`, which is not yet populated at the
point `FlowBox` resolves this same auto-margin question (image measurement runs later in the same
pass), so the fallback reads a stale/zero width and the resulting centering offset is wrong. A
`display: block` image with an explicit `width` (the common case, and the one issue #1176 fixed) is
unaffected. Filed as [issue #1178](https://github.com/jhaygood86/PeachPDF/issues/1178).
