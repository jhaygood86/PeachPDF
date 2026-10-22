# `background` shorthand: single `<box>` and `/ <size>` without a position

Both live in the per-layer converter in `BackgroundProperty`, and both are about how `WithAny`/`WithOrder` slice a layer's tokens.

**Single `<box>`.** `BoxModelConverter` (origin) and `BackgroundClipConverter` (clip) each take their own tokens from the layer, so one box keyword was always eaten by the origin and the clip converter matched nothing. The layer's clip longhand is built in `EndListValueConverter.ListValue.ExtractFor`; it now takes an optional `layerFallbacks` map (`background-clip` -> `background-origin`), consulted when a layer has no value of its own for the longhand, *before* the per-layer default. Doing it at extraction (not in the grammar) keeps `WithAny` order-independent: the two boxes need not be adjacent. `text` is a clip-only value, so the fallback copies only the origin's value, which is always a box.

**`/ <size>` alone.** `WithOrder(position.Option(), size.StartsWithDelimiter().Option())` makes both slots optional independently. A new `OrderedOptionsConverter` mode (`WithOrderDependingOnFirst`) skips the later slots when the first consumed no tokens, so the `/ 10px` stays in the list and the whole match fails; the layer — and with it the declaration — is invalid. This is the same place `WithAny`'s `VaryAll` could otherwise offer `/ 10px` to the group on its own, which is why it had to be the group that refuses, not a post-check.

Unspecified longhands read back as `initial` through the CSS-OM (the defaults are filled at paint), so the tests assert `initial` for those.

Evidence: `BackgroundShorthandGrammarTests` (origin/clip per layer and order, size accepted/dropped) and an integration test that the gradient fill is the content-box rectangle.
