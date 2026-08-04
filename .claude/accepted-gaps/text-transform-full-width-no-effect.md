# `text-transform: full-width` has no rendering effect

Tracking issue: [#638](https://github.com/jhaygood86/PeachPDF/issues/638).

Per [CSS Text Module Level 3 §2.1](https://www.w3.org/TR/css-text-3/#text-transform-property),
`full-width` should convert characters to their full-width form (used mainly for CJK typesetting
alongside Latin characters) — a transform distinct from `uppercase`/`lowercase`/`capitalize`.
`CssBox.cs`'s `ApplyTextTransform` only implements those three; `full-width` falls through to the
switch's `default: return text;` case and is a silent no-op.

`css-properties.json`'s `text-transform` entry reuses the pre-existing `Map.TextTransforms` keyword
map (already used by SVG's own text-transform handling), which includes `full-width` mapped to
`TextTransform.FullWidth` — so the value parses, cascades, and round-trips through
`getPropertyValue`/`@supports` correctly, it just doesn't change the rendered text.

`full-size-kana` (the spec's other CJK-related keyword) is not implemented at all - it isn't in the
`TextTransform` enum or `Map.TextTransforms` - so it's rejected outright and unaffected by this gap.

**Deliberately out of scope.** Fixing this means adding half-width-to-full-width Unicode form mapping
to `ApplyTextTransform` - a real text-transform behavior addition, not a doc-accuracy fix.
