# `<hr>`: an indefinite percentage height (tracked, not accepted forever)

A deviation found while fixing #1225 (`<hr>` discarding `border-style`) and deliberately left out of
that change. It is a **tracked bug to fix**, not a limitation argued through and accepted — this file
exists because `docs/html-css-support.md`'s `hr` row describes it to readers, and that note and this
file are deleted together when the issue closes.

The others that were here are all **closed**: the rule's used height (#1229's constant-2 flow advance
and container height, #1232's painted gap between the borders), its percentage `width` basis (#1230),
and the currentColor bevel base (#1226). See
[.claude/recent-fixes/2026-09-20-hr-resolves-its-own-used-height.md](../recent-fixes/2026-09-20-hr-resolves-its-own-used-height.md),
[.claude/recent-fixes/2026-09-21-hr-resolves-a-percentage-width-against-its-containing-block.md](../recent-fixes/2026-09-21-hr-resolves-a-percentage-width-against-its-containing-block.md)
and
[.claude/recent-fixes/2026-09-20-a-bevelled-currentcolor-border-shades-a-fixed-base.md](../recent-fixes/2026-09-20-a-bevelled-currentcolor-border-shades-a-fixed-base.md)
for the mechanisms, all worth reading before touching `DerivedStyle.ResolveBorderSideColor` or the shared width/height
resolvers in `CssLayoutEngine`.

## A percentage or `calc()` height against an indefinite containing block — issue #1236

**What changed since this was filed.** `<hr>` no longer resolves its own height inside its own layout pass:
`CssBoxHr` is gone (see
[.claude/recent-fixes/2026-09-21-hr-is-an-ordinary-block-not-a-box-class.md](../recent-fixes/2026-09-21-hr-is-an-ordinary-block-not-a-box-class.md)),
so `GetBoxHeight` is called once for a rule, from `ApplyHeight`, like for any other block. The
early-call/epilogue split the issue describes therefore cannot occur for a rule any more.

**What is still open** is the underlying question, and it is not `<hr>`-specific. The issue's own repro (a
parent holding only the rule) is consistent now - own height, parent height and the next block's offset all
agree at 1.5pt - but give the parent any content and the same disagreement shows up on a plain `<div>`, measured
in a parent that holds a 40pt block before the box:

| declaration | painted height | next block placed as if |
| --- | --- | --- |
| `height: 50%` | 0 (correct: computes to `auto`) | 0 |
| `height: calc(50% + 0px)` | 20 | 0 |
| `height: calc(100% - 5px)` | 36.25 | 0 |

Chrome treats every one of those as `auto` against an indefinite containing block
([CSS 2.1 §10.5](https://www.w3.org/TR/CSS21/visudet.html#the-height-property)). A `calc()` that contains a
percentage is resolved against the container's content-driven height instead, and paints at that height while
the flow reserves none.

**Root cause (confirmed by the table above, not yet fixed):** `CssLayoutEngine` detects "this height is a
percentage" with `.EndsWith('%')` at a dozen sites (`GetBoxHeight`, `HasDefiniteHeight`,
`ResolveDefiniteHeightValue`, `ApplyHeight`'s max-height clamp, and the min/max-height reads). A plain `50%` is
caught; `calc(100% - 5px)` ends in `)`, is read as a definite length, and is resolved against
`heightCb.Size.Height`. The fix is one helper that answers "does this length depend on a percentage" (a `%`
suffix, or a `calc()`/`min()`/`max()`/`clamp()` containing one) replacing each `EndsWith('%')`. That reaches every
`GetBoxHeight` caller, so it needs its own measurement pass rather than riding along with a rule-specific change.

Deleted together with the limitation sentence in the `hr` row of `docs/html-css-support.md` when #1236 closes.
