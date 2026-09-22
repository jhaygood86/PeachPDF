# `border-style: hidden` no longer reserves space, and `column-rule-style: hidden` is no longer drawn

**Before:** a side declared `hidden` was never painted, but its declared `border-*-width` was still
kept as the used width. The edge therefore occupied its full width in the box model: a
`border: 16pt hidden` box laid out 32pt wider and 32pt taller than the same box with
`border: 16pt none`, and — since the background paints over the border box — the extra space showed
up as a visibly larger background area with nothing drawn in it.

`column-rule-style: hidden` was wrong in a different way. A column rule is painted in the middle of
the column gap and has never taken space out of it, so nothing about the container's geometry
changed — but a `hidden` rule was **drawn**, as a solid line of its full declared width, because the
only thing keeping a rule off the page was a zero used width and `hidden` did not produce one. So
`column-rule: 16pt hidden` painted exactly what `column-rule: 16pt solid` painted.

**Now:** the used width of a `hidden` side is 0, exactly as for `none`
([css-backgrounds-3 §3.2](https://www.w3.org/TR/css-backgrounds-3/#border-style):
"same as `none`, but…"; [§3.3](https://www.w3.org/TR/css-backgrounds-3/#the-border-width): computed
value is "absolute length, or 0 if the border style is `none` or `hidden`"). A `hidden` border edge
is neither painted nor given any space, and a `hidden` column rule is not drawn at all — the same
rule for `column-rule-width` in
[css-multicol-1 §4.4](https://www.w3.org/TR/css-multicol-1/#crw).

**Unchanged:** `hidden`'s one real difference from `none` — a border-collapsed table's conflict
resolution ([CSS 2.1 §17.6.2](https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution),
clause 1, where `hidden` suppresses the shared grid line and beats every competing declaration) —
still works. The resolver reads the declared *style* alongside the width and short-circuits on
`hidden` before any width comparison, so zeroing the width does not weaken it.

**What an author may need to change:** a layout that (knowingly or not) leaned on a `hidden` border
to reserve space — the "invisible spacer border" idiom — now collapses. Declare the space as
`padding`, or as a `transparent` border, both of which keep their width. Separately, a document that
used `column-rule-style: hidden` and was relying on the line it (incorrectly) drew should say
`solid` instead.
