# A border side's cached `ActualBorder*Color` is invalidated only by its own longhand

`DerivedStyle` caches each side's used colour in `_actualBorderTopColor` and its three siblings, and
the only thing that clears one is `InvalidateBorderTopColor`/`…Right…`/`…Bottom…`/`…Left…`, which the
generated `CssPropertyRegistry` calls when **that side's `border-*-color`** is written
(`css-properties.json`, each entry's `"invalidates"`). Nothing else clears them.

Since #1226 that cache is a function of **four** inputs, not one:

| input | invalidates the colour cache? |
| --- | --- |
| `border-<side>-color` | yes |
| `border-<side>-style` | **no** — its `invalidates` hook names `InvalidateBorder<side>Width`, which clears the *width* cache only (see [hidden is zero-width…](css-hidden-is-a-zero-used-width-so-the-style-longhand-must-invalidate-the-width-cache.md)); nothing clears the colour cache |
| `color` | **no** — `InvalidateColor` clears `_actualColor` only |
| `display` (via `ActualDisplay`, the table exemption) | **no** — `display` has no `invalidates`, and `ActualDisplay` is itself uncached |

`ResolveBorderSideColor` reads all four: a side whose colour is still the literal `currentcolor`
resolves to `BorderBevelColors.CurrentColorBase` when the side is bevelled and the box is not a table
display type, and to `ActualColor` otherwise.

## The rule

**Do not read `ActualBorderTopColor` or its siblings before the cascade has settled**, and do not
introduce a path that mutates `color`, `display` or a `border-*-style` longhand after something has
read one. The first read freezes the answer for the life of the box.

## Why it is safe today, and how thin that is

Every reader is in paint or in a border-geometry decision, all of which run after `DomParser` has
finished the box's cascade, and no code mutates `color` or a `border-*-style` after it. `display` is
the one that is *already* mutated mid-layout and merely happens not to matter: `CssLayoutEngineFlex`
and `CssLayoutEngineGrid`'s `PerformLayoutBlockified`, and `ItemContentCommit`, each set
`box.Display = block` around a child's layout and restore it afterwards. Today that window only ever
swaps `inline`→`block`, neither of which is a table display type, so the exemption's answer does not
change inside it — but a border colour read during that window would cache a resolution taken against
a display the box does not have, and the restore would not undo it.

That is the shape to watch for: the divergence is silent, survives for the rest of the render, and
shows up as one box painting `#eeeeee` where its neighbour paints the text colour.

## If a reader has to move earlier

Give the three missing inputs an `invalidates` hook rather than reaching for the cached property from
a new place. `border-*-style` already carries one, so it needs a *second* entry
(`InvalidateBorder<side>Color` alongside the existing `InvalidateBorder<side>Width` — the schema takes
a list); `display` would need its first; and `InvalidateColor` would have to clear the four border
caches as well as `_actualColor`. That is a cheap change; the expensive part is noticing it was
needed.
