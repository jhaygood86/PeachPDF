# `<hr>`: percentage width and indefinite percentage height (tracked, not accepted forever)

Deviations found while fixing #1225 (`<hr>` discarding `border-style`) and deliberately left out of
that change. Both are **tracked bugs to fix**, not limitations argued through and accepted — this file
exists because `docs/html-css-support.md`'s `hr` row describes them to readers, and those notes and
this file are deleted together when the issues close.

The two that were here on the rule's used height — #1229's constant-2 flow advance and container
height, and #1232's painted gap between the borders — are **closed**, fixed together in
`CssBoxHr.PerformLayoutImp`. See
[.claude/recent-fixes/2026-09-20-hr-resolves-its-own-used-height.md](../recent-fixes/2026-09-20-hr-resolves-its-own-used-height.md)
for the mechanism, which is worth reading before touching that method.

The third, #1226's currentColor bevel base, is **closed** too: `CssUtils.ApplyCurrentColor` now
resolves a bevelled border side's `currentColor` against the fixed light base itself, so the UA sheet
no longer declares a colour at all and the `border-style: dashed` residue this file used to describe
is gone. See
[.claude/recent-fixes/2026-09-20-a-bevelled-currentcolor-border-shades-a-fixed-base.md](../recent-fixes/2026-09-20-a-bevelled-currentcolor-border-shades-a-fixed-base.md).

## A percentage `width` resolves against the wrong basis — issue #1230

`CssBoxHr.PerformLayoutImp` builds the percentage basis as the containing block's content width minus
the rule's *own* left and right border widths, then passes it to `CssValueParser.ParseLength`. CSS 2.1
[§10.2](https://www.w3.org/TR/CSS21/visudet.html#the-width-property) resolves against the containing
block's content width, with the element's own border outside it. Those two subtrahends belong to the
`auto` branch, which is correct and is what an unstyled rule uses.

Measured, in a 200pt containing block with `border:4px solid` (3pt per side):

| declaration | `<div>` content / border box | `<hr>` content / border box |
| --- | --- | --- |
| `width: 50%` | 100 / 106 | **97 / 103** |
| `width: 100%` | 200 / 206 | **194 / 200** |

A browser agrees with the `<div>` column.

**The trap this sets.** Pairing a rule against its equivalent zero-height `<div>` — the natural way to
test or showcase rule painting — is only valid with no `width` declared at all. With `width: 100%` the
two differ by twice the border width, the div's border box overflows its cell, and the pair looks
broken for a reason that has nothing to do with what is being demonstrated. That is exactly how the
first version of the `border_style` showcase's `<hr>` section shipped four mismatched pairs.

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
