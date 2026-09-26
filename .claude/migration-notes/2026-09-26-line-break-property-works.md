# `line-break` now works

**Before:** `line-break` was an unknown property and had no effect: text always broke with the strictness of `normal`.

**Now:** `line-break: auto | loose | normal | strict | anywhere` is parsed, inherited and applied to where a line may end (`auto` is `normal`, so a document that did not use the property is unchanged). `strict` and `normal` keep a small kana with the character before it, `loose` lets one start a line, and `anywhere` lets a line end after every character, which also narrows the min-content width of words. `@supports (line-break: anywhere)` is now true.
