# `@keyframes` lookup: cascade layers order and quoted names

`RegisteredKeyframes.BuildRegistry` used to let the last `@keyframes` of a name win in flattened source order, so an
`@layer` rule after an unlayered one replaced it, and `@keyframes "k"` parsed to an empty name (the composer only read an
identifier) and was dropped. Now `CssData.EnumerateKeyframesWithLayerRank` walks the grouping rules with its own
`LayerRegistry` (same registration steps as `IndexRules`: statements, named, nested and anonymous layers) and the registry
takes the highest rank per name, the later rule among equals; unlayered is `UnlayeredRank`. `StylesheetComposer.CreateKeyframes`
reads a `<string>` name. `animation-name` already unquoted strings, so the lookup key is the bare text either way.

Trap: the ranks come from a second, separate registry rather than the one in `IndexRules`, because the keyframes registry is
built from `DomParser` without the indexing pass's state. If the layer registration rules in `IndexRules` change, change
`CollectKeyframes` with them. Evidence: `AnimationKeyframesLookupTests` (layer order, nesting, anonymous, `@media`/`@supports`
inside a layer, string names).
