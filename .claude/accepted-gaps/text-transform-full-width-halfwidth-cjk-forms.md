# `text-transform: full-width` doesn't convert halfwidth katakana/Hangul jamo/symbol forms

Tracking issue: [#672](https://github.com/jhaygood86/PeachPDF/issues/672).

Per [CSS Text Module Level 3 §2.1](https://www.w3.org/TR/css-text-3/#text-transform-property),
`full-width`'s mapping is defined by Unicode Standard Annex #44's `Decomposition_Mapping`: code
points tagged `<wide>` **or** `<narrow>`.

`CssBox.cs`'s `ApplyTextTransform`/`ToFullWidth` implements the `<wide>`-tagged half of that
mapping: ASCII `!`-`~`, space, and the Latin-1 currency/symbol characters (¢£¬¯¦¥₩) that have a
fullwidth compatibility form — the case the spec calls out as typical ("typeset Latin letters and
digits as if they were ideographic characters"), and the one [#638](https://github.com/jhaygood86/PeachPDF/issues/638)
was filed against.

It does not implement the `<narrow>`-tagged half: halfwidth katakana (U+FF61-FF9F), halfwidth
Hangul jamo (U+FFA0-FFDC), and halfwidth symbol variants (U+FFE8-FFEE) are left untransformed.
Each of these has a well-defined singleton compatibility decomposition to its normal-width form
(so implementing them would stay length-preserving, same as the ASCII case), but this is legacy
JIS X 0201-style halfwidth-CJK content — a narrower and less common case than the Latin-alongside-CJK
use case #638 was about, similar in spirit to `full-size-kana` (which #638 itself carved out as
unrelated).

**Deliberately out of scope.** Fixing this means adding the ~130-entry halfwidth-katakana/Hangul-jamo/
symbol lookup table to `ToFullWidth` - a real but narrow scope extension, not a doc-accuracy fix.
