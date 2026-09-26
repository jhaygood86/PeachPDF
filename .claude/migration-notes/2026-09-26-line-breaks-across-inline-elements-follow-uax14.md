# Line breaks across inline element boundaries follow UAX #14

**Before:** the last word of one inline element and the first of the next (`foo<b>bar</b>`, `<b>abc-</b><i>123</i>`) were decided by an older approximation: a break needed white space, a hyphen, an ideograph or an emoji beside it, plus a table of opening and closing punctuation.

**Now:** the two words are analysed together by the Unicode line breaking algorithm, with the white space the markup collapsed between them put back and the `word-break` and `line-break` of either element applied. Mostly nothing moves. What changes is where the old rule and the algorithm disagreed: a hyphen followed by a digit in the next element no longer breaks (`<b>abc-</b><i>123</i>`, as `abc-123` in one element never did), and `line-break: anywhere` and `word-break: break-all` now break between elements as well.
