# `hidden` is a zero used width, so the collapsed resolver must read style before width

`border-style: hidden` and `column-rule-style: hidden` compute a used width of **0**, exactly like
`none` ([css-backgrounds-3 §3.3](https://www.w3.org/TR/css-backgrounds-3/#the-border-width):
"absolute length, or 0 if the border style is `none` or `hidden`"). `DerivedStyle` enforces that in
one place — the four `ActualBorder*Width`, the four `NaturalBorder*Width`, and
`ActualColumnRuleWidth` each test `is LineStyle.None or LineStyle.Hidden`.

## The rule that makes that safe

`hidden`'s whole reason to exist is that it is **not** `none` inside a `border-collapse: collapse`
table: [CSS 2.1 §17.6.2](https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution) clause 1
says it suppresses the shared grid line and beats every competing declaration *regardless of width*.
A zero width would lose that fight outright under clause 3 ("widest wins").

It does not, because **the resolver decides on style before it ever compares widths**:
`CollapsedBorderModel.Add` reads the declared style off the box and carries it into the candidate
alongside the width, and `CollapsedBorderResolver.Resolve` returns on
`candidate.Style == LineStyle.Hidden` before `Beats` runs. `CollapsedBorder.UsedWidth` independently
reports 0 for the same pair.

**Any change that makes the resolver consult a candidate's width before its style re-breaks this** —
a `hidden` border would stop suppressing its grid line and the widest ordinary border would win the
segment instead. The symptom is a collapsed table that should have a missing edge drawing a normal
one, which reads as a styling mistake rather than a resolver bug.

## The invalidation half

The used width is cached on first read and is a function of **two** cascade inputs, the width
longhand and the style longhand. Both must carry an `invalidates` hook in `css-properties.json`, and
since this was written both do: `border-<side>-width` → `InvalidateBorder<side>Width`, and
`border-<side>-style` → the same method. Before that second hook existed, reading a width and then
writing the style left the stale value in place for the life of the box — pre-existing for `none`,
and `hidden` simply gave it a second style that changes the answer.

`ActualOutlineWidth` has the identical shape — cached on first read, zeroed for
`OutlineStyle.None` — and carries the same pair of hooks: `outline-width` → `InvalidateOutlineWidth`,
and `outline-style` → the same method.

**The full inventory of width caches and their hooks**, so this reads as a complete list rather than
a sample:

| cache | width longhand | style longhand |
| --- | --- | --- |
| `_actualBorder<side>Width` (×4) | yes | yes |
| `_actualOutlineWidth` | yes | yes |
| `_actualColumnRuleWidth` | **no** | **no** |

`column-rule-width` is the only one still missing this, and it is missing *both*:
`_actualColumnRuleWidth` has no invalidator at all, and `DerivedStyle` has no
`InvalidateColumnRuleWidth` method to hook up — unlike the border and outline cases, closing it means
writing the method first, not just adding a JSON line. That is why
[the hidden paint guards](paint-the-hidden-style-guards-are-deliberately-unreachable.md) state the
suppression rule at the paint site rather than inferring it from the cached width.

The logical longhands (`border-block-start-style` and friends) need no hook of their own: they are
read-once resolution scratch space that `CssBox.ResolveLogicalProperties` writes through to the
physical longhand, whose setter fires the hook.

Related: [a border side's cached colour](dom-a-border-sides-cached-actual-colour-is-invalidated-only-by-its-own-longhand.md),
which depends on the style longhand too but is **not** cleared by this hook.
