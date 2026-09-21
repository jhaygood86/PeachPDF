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
for the mechanisms, all worth reading before touching `CssBoxHr.PerformLayoutImp` or
`DerivedStyle.ResolveBorderSideColor`.

## A percentage or `calc()` height against an indefinite containing block — issue #1236

`<hr>` is the only box that resolves its own height inside its own layout pass, because it has to: the
frame commits the *next* sibling's offset against that bottom, and the layout epilogue runs too late
to be what the sibling sees (see
[.claude/recent-fixes/2026-09-20-hr-resolves-its-own-used-height.md](../recent-fixes/2026-09-20-hr-resolves-its-own-used-height.md)).
That means `CssLayoutEngine.GetBoxHeight` is called twice for a rule — once early, once from
`ApplyHeight` — and against an **indefinite** containing block the two calls do not agree
(~1.5pt early against ~26.35pt in the epilogue for `height: calc(100% - 5px)`). The rule paints one
height and the flow reserves another.

Left out of #1233 because the fix is not in `CssBoxHr`: `GetBoxHeight` should return `null` for a
percentage against an indefinite containing block — the contract `ApplyHeight` already documents for
that case, and what [CSS 2.1 §10.5](https://www.w3.org/TR/CSS21/visudet.html#the-height-property)
means by "computes to `auto`". That reaches every caller of `GetBoxHeight`, so it needs its own
measurement pass rather than riding along with a rule-specific change.

**Not a new split.** The same class of disagreement predates #1233, which changed which value the
early call produces without introducing the two-call structure.
