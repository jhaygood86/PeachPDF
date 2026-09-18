# `text-align-last: justify` now centers a line it cannot stretch

Previously, a line governed by `text-align-last: justify` that had no justification opportunity at
all to stretch over — a single unbreakable run, or two inline boxes with no white space between them
(`A<span>B</span>`) — was start-aligned: the physical left edge under `direction: ltr`, the right edge
under `rtl`.

It is now centered instead, per [css-text-3 §6.4.3](https://www.w3.org/TR/css-text-3/#justify-algos)'s
parenthetical: "If `text-align-last` is `justify`, then they must be aligned as for `center`." This is
a deliberate spec-literal choice, not a bug fix following new evidence about the spec text — Chromium,
Firefox, and Safari all start-align this case rather than implementing the parenthetical, and PeachPDF
previously matched them intentionally (see the now-deleted
`.claude/accepted-gaps/unexpandable-justified-line-starts-rather-than-centres.md`). The repo owner
decided to follow the spec's literal text instead, making PeachPDF diverge from all three browser
engines on this one case.

This only affects a line with **zero** justification opportunities under `text-align-last: justify`.
An ordinary justified line, and a line ending a paragraph that has at least one opportunity to
stretch over, are unaffected.
