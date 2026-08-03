# `text-indent`'s `hanging`/`each-line` keywords are not supported

Tracking issue: [#607](https://github.com/jhaygood86/PeachPDF/issues/607).

Per [CSS Text Module Level 3 §3](https://www.w3.org/TR/css-text-3/#text-indent-property),
`text-indent`'s full grammar is `<length-percentage> && hanging? && each-line?`: `hanging` inverts
which line of the paragraph is indented (the first line stays flush, the rest are indented by the
given amount), and `each-line` applies the indent after every forced line break, not just the block's
own first line.

PeachPDF implements only the bare `<length-percentage>` — `css-properties.json`'s `text-indent` entry
declares `cssDataType: "length"` with no keyword clause, matching reality: neither `hanging` nor
`each-line` is parsed or handled anywhere in the layout code. `docs/html-css-support.md`'s
`text-indent` row previously claimed unqualified "Full support"; corrected to note only the length/
percentage value is supported.

**Deliberately out of scope.** Fixing this means adding both keywords to the grammar and implementing
their line-selection logic in the block/line-box layout path — a real layout feature, not a doc-
accuracy fix.
