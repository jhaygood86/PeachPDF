# `line-break: loose` is not language-aware and lacks the East Asian prefix and suffix breaks

`LineBreakOptions.Strictness` implements the parts of CSS Text 3's `line-break` that need no language: `CJ` (small kana and the
prolonged sound mark) is a non-starter except in `loose`, iteration marks, inseparable characters and the centred punctuation
are line starters only in `loose`, and a hyphen may start a line in `loose` only after an ideograph.

Two things are missing. The rules the specification limits to a Chinese or Japanese writing system (U+301C and U+30A0 for
`normal` and `loose`, the centred punctuation for `loose`) are applied whatever the language, because the entry point takes
none. And the `loose` breaks before East Asian suffixes and after East Asian prefixes (`PO` and `PR` characters whose East Asian
Width is Ambiguous, Fullwidth or Wide) are not implemented, because the generated tables carry only the wide, fullwidth and
halfwidth flag. Because the tailoring is language-blind, `loose` also breaks before an ellipsis or an exclamation mark that follows
Latin text. Tracked in [#1401](https://github.com/jhaygood86/PeachPDF/issues/1401).
