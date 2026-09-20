# `<hr>`: auto height, flow advance, percentage width, and the currentColor bevel base (tracked, not accepted forever)

Deviations found while fixing #1225 (`<hr>` discarding `border-style`) and deliberately left out of
that change. All are **tracked bugs to fix**, not limitations argued through and accepted — this file
exists because `docs/html-css-support.md`'s `hr` row now describes the first three to readers, and
that note and this file are deleted together when the issues close.

## An auto-height rule is always 2pt tall, and paints the remainder as a gap — issue #1232

The shared root cause of this section and the next one. `CssBoxHr.PerformLayoutImp`'s height fallback
reads `Size.Height + ActualBorderTopWidth + ActualBorderBottomWidth`, but `PlaceAsBlockChild` has
already written `ActualBottom = Location.Y` by then, and that setter stores
`Size.Height = value - ActualBoxSizeIncludedHeight - Location.Y` — so `Size.Height` is exactly
`-(borderTop + borderBottom)`. Instrumented: `-1.5` with the default 0.75pt borders, `-3` with 2px
ones, `ActualHeight` `0` in both. The sum cancels to zero every time, so the fallback is unreachable
and the hard-coded `height = 2` below it is what every auto-height rule gets.

Two things fall out. The leftover `max(0, 2pt - borders)` is used **content**, so it paints as a white
gap between the two border edges — 0.5pt on the 1px default, and a full 1pt on a `border: 0.5pt` rule,
wider than either border. And the same constant 2 is what the flow arithmetic below sees.

Dropping the poisoned term (`height = ActualBorderTopWidth + ActualBorderBottomWidth`) fixes the gap
and the auto-height half of #1229; measured, with the full suite passing. It was deliberately not
folded into #1225, which is a paint-path change: it moves every default rule's height from 2pt to
1.5pt, and that is a user-visible change wanting its own migration note rather than riding along with
an unrelated one.

## The following block is placed 2 units below the rule's *top* — issue #1229

CSS 2.1 [§10.6.3](https://www.w3.org/TR/CSS21/visudet.html#normal-block) computes a block container's
content height from its last in-flow child's bottom margin edge, and
[§8.3.1](https://www.w3.org/TR/CSS21/box.html#collapsing-margins) with
[§10.5](https://www.w3.org/TR/CSS21/visudet.html#the-height-property) put a following in-flow sibling
at the box's border-box bottom plus the collapsed margin. (§9.4.1's "one after the other, vertically"
is too loose to pin either.) An `<hr>` advances the flow by a constant 2 units whatever its
resolved height is, so anything after a rule with a border thicker than about 1px lands on top of it.

Measured: with `<hr style="margin:0; border:Npx solid">` at `Location.Y == 26`, the following
`<div>`'s `Location.Y` is 28 for every N tested (1, 2, 3, 6, 10) and for `height:10px` as well, while
the rule's own `ActualBottom` correctly reads 28 / 29 / 30.5 / 35 / 41 / 42.5. An equivalent
zero-height `<div>` puts the follower at 35. The rule's fragment `WholeBoxRect` is the right height
too — only the *following* sibling's placement ignores it.

The constant 2 is the `height = 2` above. Removing that fixes this for an auto-height rule (a
`border: 6px` rule's follower moves from +2 to +9, matching Chrome) but **not** for one with a declared
`height`, whose follower still advances by the border total — it is placed on an earlier pass and never
re-placed once the real height resolves. That remaining half is a genuine pass-ordering problem.

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
