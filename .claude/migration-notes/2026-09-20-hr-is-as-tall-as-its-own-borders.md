# An `<hr>` is as tall as its own borders, not a constant 2pt

A rule's used height used to be 2 units regardless of its borders or its `height`, and so were the
two things measured against it: where the next block starts, and how tall a container holding only
the rule is. All three now follow the rule's real height.

## A default rule no longer has a white line through it

The clearest case, and the one most documents hit. A 2pt box with 1.5pt of border left 0.5pt over as
*content*, which painted as a gap between the two border edges — three bands at high zoom where a
browser draws two.

| | before | now | Chrome |
| --- | --- | --- | --- |
| `<hr>` | `#9a9a9a`, **0.5pt white**, `#eeeeee` | `#9a9a9a`, `#eeeeee` | `#9a9a9a`, `#eeeeee` |
| total height | 2pt | **1.5pt** | 2px (= 1.5pt) |

A thinner author border was affected more, not less: the gap was `2pt − borders`, so
`<hr style="border: 0.5pt solid">` had a 1pt gap, wider than either border it separated.

## A rule no longer overlaps what follows it, or overflows its container

```html
<div><hr style="margin: 0; border: 10px solid"></div>
<p>after</p>
```

| | before | now |
| --- | --- | --- |
| the rule's own height | 15pt | 15pt |
| where `<p>` starts | 2pt below the rule's top | **15pt** |
| the `<div>`'s height | 2pt | **15pt** |

So a bordered rule no longer has following content painted over it, and a container sized by its
content is no longer 2pt tall whatever it holds. Any layout that worked around this — an explicit
container height, a margin sized to clear the overlap — will now have that workaround *added* to
correct spacing.

## `height` on a rule now takes effect in the same pass

```html
<hr style="margin: 0; height: 10px"><p>after</p>
```

The rule was always 9pt tall (7.5pt content plus the UA's 1.5pt of border), but `<p>` started 2pt
below its top. It now starts 9pt below. The declared height reached the rule only after the following
block had already been placed against it.

## Heights, before and after

Points, at 1px = 0.75pt, on an `<hr>` with `margin: 0`:

| declaration | before | now |
| --- | --- | --- |
| *(default)* | 2 | **1.5** |
| `border: 0.5pt solid` | 2 | **1** |
| `border: 2px solid` | 3 | 3 |
| `border: 6px groove` | 9 | 9 |
| `height: 0` | 1.5 | 1.5 |
| `height: 10px` | 9 | 9 |
| `border: none` | 2 | 2 |

Only rules whose borders total under 2pt change height, and they *lose* the leftover content — which
is what closes the gap. Everything from 2pt of border upward was already correct and is untouched. A
rule with neither a border nor a height keeps its nominal 2pt box: a zero-size box produces no
fragment at all, so collapsing it would remove the rule from the document rather than just make it
invisible.

## Why

An auto-height block-level box with no in-flow children has a used content height of 0
([CSS 2.1 §10.6.3](https://www.w3.org/TR/CSS21/visudet.html#normal-block)), so a rule is exactly its
own two horizontal borders; and a following in-flow sibling starts at the previous box's border-box
bottom plus the collapsed margin
([§8.3.1](https://www.w3.org/TR/CSS21/box.html#collapsing-margins) with
[§10.5](https://www.w3.org/TR/CSS21/visudet.html#the-height-property)). `<hr>` was the one box in the
document that followed neither.
