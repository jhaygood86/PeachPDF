# A nested table's row moved to a page top draws its last line twice

Tracking issue: [#1795](https://github.com/jhaygood86/PeachPDF/issues/1795), which has the same root cause: a nested table breaks to
the page top without the room its outer table's repeated header takes there, and the row that then crosses the page foot is
drawn on two pages.

A table inside another table's cell, whose row is moved to the next page (`break-before: page` on it, or the
straddle correction) and still does not fit there, lays its last line out on two pages: once at the foot of the
page it moved to, and again under the outer table's repeated header on the next. It is on `main` independently of
[#1835](https://github.com/jhaygood86/PeachPDF/issues/1835) (found in its review). css-break-3 §2 places each
piece of fragmented content in one fragmentainer; repeating it is a deviation.

**Mechanism, measured on the review's repro** (300pt page, nested row of 22 `<br>` lines, outer `<thead>`): the
inner engine moves its row to the page top in document space, unaware of the outer header. The outer row holding
it then reaches past the band, no band holds it, and `SliceARowAcrossTheBandsItOverflows` displaces it under the
repeated header on each band. The nested row's last line, already at the band foot, is pushed past it by the
header's height (`R010L26` at 275.8..287.3 against a band bottom of 280) and is claimed by both strips. In one
review document the nested row's first line landed under the repeated header and was drawn on no page.

**Why it was left:** the fix belongs where an inner table's break meets the outer table's slice: the inner
engine would need the room the outer table's repeated groups take on the band it moves to, which the cursor
does not carry across tables. That is a change to how nested fragmentation composes, not to #1835.

**What #1835 did instead:** `CssLayoutEngineTable.AvoidedBreakCorrectionAppliesTo` declines a table inside a
table cell, so `break-inside: avoid` on a nested table's rows does not make the shape reachable from an ordinary
rule (a global `tr { break-inside: avoid }` reaches nested rows). Such a row is split at the page foot, as it was
before #1835 — a second, smaller deviation from css-break-3 §3.2, under the same issue.
`TableRowBreakInsideAvoidTests.ANestedRowThatAvoidsBreaksAndStopsAgain_IsLaidOutOnce` pins the avoid case; the
`break-before: page` variant still repeats the line.
