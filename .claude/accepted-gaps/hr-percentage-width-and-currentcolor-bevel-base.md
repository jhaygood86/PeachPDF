# `<hr>`: percentage width, indefinite percentage height, and the currentColor bevel base (tracked, not accepted forever)

Deviations found while fixing #1225 (`<hr>` discarding `border-style`) and deliberately left out of
that change. Both are **tracked bugs to fix**, not limitations argued through and accepted — this file
exists because `docs/html-css-support.md`'s `hr` row describes them to readers, and those notes and
this file are deleted together when the issues close.

The two that were here on the rule's used height — #1229's constant-2 flow advance and container
height, and #1232's painted gap between the borders — are **closed**, fixed together in
`CssBoxHr.PerformLayoutImp`. See
[.claude/recent-fixes/2026-09-20-hr-resolves-its-own-used-height.md](../recent-fixes/2026-09-20-hr-resolves-its-own-used-height.md)
for the mechanism, which is worth reading before touching that method.

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

## An inset/outset border whose colour is `currentColor` is beveled from the wrong base — issue #1226

Blink does not shade a `currentColor` border from `currentColor`; it shades a fixed light base. That is
why Chrome paints an unstyled `<hr>` `#9a9a9a` over `#eeeeee` rather than gray's own
`#2c2c2c`/`#d4d4d4`, and why `<div style="border: 2px inset; color: red">` paints those same two greys
rather than anything red. PeachPDF has no "this border colour came from currentColor" signal in the
cascade, so it shades whatever colour it is given.

#1225 works around it for the one case it would otherwise have regressed: the UA sheet declares
`hr { border: 1px inset #eee }`, `#eee` being the base whose two faces *are* `#9a9a9a` and `#eeeeee`.
That keeps a default rule byte-identical to a browser's and to v0.9.19's.

**The residue this leaves**, and the reason it is written down: an author who sets `hr { border-style:
dashed }` and nothing else gets `#eee`, where Chrome gives gray. A rule's declared base is only the
right colour while it is being beveled, and the `hr[color], hr[noshade]` arm hands the other
non-beveled case back to `currentcolor` precisely so it does not have the same problem — but an
author-set `border-style` cannot be enumerated that way. Nothing short of implementing Blink's actual
rule fixes it, which is also what would fix the general `<div style="border: 2px inset">` case, and is
why #1226's scope is now the cascade signal rather than the `hr` colours it was originally filed
about.

**Do not "simplify"** `hr { border: 1px inset #eee }` to the spec's literal `color: gray` +
`currentColor` while this is open. It reads like the more correct thing and it repaints every default
rule on the web `#2c2c2c`.

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
