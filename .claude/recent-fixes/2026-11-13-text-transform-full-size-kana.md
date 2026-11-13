# `text-transform: full-size-kana`

The keyword was rejected outright. `TextTransform.FullSizeKana` (Keywords, enum, `Map.TextTransforms`, the property's `supportsSupportedValues`) and
`TextTransformer.ToFullSizeKana`, one code point to one code point like `full-width`, so the word and whitespace indices computed on the transformed text stay valid.

Not done: combining it with a case keyword (`uppercase full-size-kana`); the property is still a single-keyword enum, as it was for `full-width`.
Evidence: WPT css-text `text-transform-full-size-kana-001..004` (+4), none lost; `TextTransformerFullSizeKanaTests`.
