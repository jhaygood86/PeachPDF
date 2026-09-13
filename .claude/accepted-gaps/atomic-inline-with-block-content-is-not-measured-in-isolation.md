# An atomic inline whose content is block-level is not measured in isolation

Tracked as **#1032**. Found while fixing issue #1017; pre-existing, and unaffected by it either way.

`CssBox.GetMinMaxSumWords` carries **one** running line total across a whole subtree, so an atomic
inline-level box is not measured as a unit: the walk descends into it, and a **block-level** child
inside it resets that total — discarding whatever was already on the outer line — after which the
epilogue restores it with `Math.Max` rather than a sum. An atomic inline contributes its own
max-content width to the line it sits on, as one unit
([css-display-3 §2.3](https://www.w3.org/TR/css-display-3/#atomic-inline),
[css-sizing-3 §5.2](https://www.w3.org/TR/css-sizing-3/#intrinsic-contribution)). The walk computes
`max(outer line, inner content)` where it must compute `outer line + inner content`.

Float widths at `font: 16px monospace` (one character advances 6.5977pt), measured through the
`IntrinsicWidthWalkTests` harness:

| markup inside `<div style="float:left">` | measured | expected |
|---|---|---|
| `AB <span style="display:inline-block">CD</span>` | 32.9883 | 32.9883 ✅ |
| `AB <span style="display:inline-block"><div>CD</div></span>` | **19.7930** | 32.9883 |
| `AB <span style="display:inline-block"><div>CD EF GH</div></span>` | **52.7812** | 72.5742 |
| `AB <span style="display:inline-block"><div>CD</div><div>EF GH</div></span>` | **32.9883** | 52.7812 |
| `AB <span style="display:inline-table"><div>CD</div></span>` | **19.7930** | 32.9883 |
| `AB <span style="display:inline-grid"><div>CD</div></span>` | **19.7930** | 32.9883 |
| `AB <span style="display:inline-flex"><div>CD</div></span>` | 32.9883 | 32.9883 ✅ |

Every wrong row is exactly `max("AB " = 19.7930, inner max-content)`. The first row is right only
because there is no block-level child to trigger the reset, and a box with nothing before it on the
line (`<span style="display:inline-block"><div>CD EF</div></span>` alone → 32.9883) is right for the
same reason — there is nothing to discard. Expected values are derived from the spec and corroborated
by PeachPDF's own `inline-flex` path below; no browser was available to the session that measured this.

The consequence is the usual one for an undercount out of this walk: a shrink-to-fit box (a float, a
`position: absolute` box, an auto table column) sized narrower than the run it holds, which then draws
over its neighbour.

## `inline-flex` is already correct, and is the oracle for the fix

The last row is the same markup with the same inner block content, and it comes out right — because
`IsFlexRow` measures each flex item by calling the item's own top-level `GetMinMaxWidth` and adding the
result, precisely because one running line total cannot express "this is measured on its own and then
sits on the line". That branch exists for a flex row's *several* items; an atomic inline is the same
situation with one participant, and the same treatment gives the same correct answer. **This needs no
new mechanism, only applying the existing one** — including its `trailingSpace = 0` (the space before
the box stops being trailing the moment the box lands on the line; see
[.claude/invariants/intrinsic-a-trailing-space-may-only-hang-where-a-line-is-known-to-end.md](../invariants/intrinsic-a-trailing-space-may-only-hang-where-a-line-is-known-to-end.md)).

## The trap that makes it more than a one-liner

`paddingSum` is a **separate** running total that is not scoped the same way, so measuring the box in
isolation moves its border/padding into the returned number while the old contribution is still in
`paddingSum`, and it is then counted twice. A partial version of the same bug is visible today:

```html
AB <span style="display:inline-block"><div style="padding-left:20pt">CD</div></span>
```

measures 39.7930 = `max(13.1953, 19.7930) + 20` — the padding is added even though the content width it
belongs to was discarded. Expected 52.9883.

`min` (min-content) needs the isolated box's min-content too, rather than continuing the outer
unbreakable run through it, since an atomic inline is itself an unbreakable unit on the line.

## Why it was out of scope for #1017

#1017 was `StartsNewLine` conflating `white-space` with block-level-ness. The numbers above are
identical before and after that change, because the discard happens at the block-level *child's*
reset, not at the atomic inline's own boundary — different cause, different fix.
