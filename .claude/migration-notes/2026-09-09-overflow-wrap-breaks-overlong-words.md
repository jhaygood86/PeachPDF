# `overflow-wrap` now breaks overlong words

`overflow-wrap` and its legacy `word-wrap` alias were previously accepted by the CSS parser but had
no effect, so an unbreakable word could extend beyond its box even when `break-word` was requested;
`anywhere` was rejected altogether.

Both names now support `normal`, `break-word`, and `anywhere`. The two emergency values move a word
to an ordinary wrap opportunity first, then break it at an extended grapheme-cluster boundary if it
still cannot fit, without adding a hyphen. `anywhere` also reduces the word's min-content
contribution, while `break-word` retains the unbroken min-content width.
