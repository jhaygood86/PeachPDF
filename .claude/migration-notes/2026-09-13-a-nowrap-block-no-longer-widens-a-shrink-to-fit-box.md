# A `white-space: nowrap` block no longer widens a shrink-to-fit box, and an inline-level box no longer needs `nowrap` to share a line

One change a document author can see, in the intrinsic (max-content) width that sizes a float, a
`position: absolute` box, an auto table column, and a flex item. Issue #1017.

## A `nowrap` block-level sibling gets its own line again

**Before:** the measurement treated `white-space: nowrap` as saying the box does not begin a line, so
a `nowrap` block-level box had its line **added** to the previous sibling's instead of competing with
it for "widest line wins". The error was one whole sibling line each time, so it grew with the sibling
count:

```html
<div style="float:left; white-space:nowrap">
  <div>AB CD</div><div>EF</div><div>GH</div>
</div>
```

measured 59.3789pt at `font: 16px monospace` where a browser measures 32.9883pt — the float was sized
for all three lines laid end to end. The box's own border and padding lost the same per-line scoping,
so two bordered `nowrap` siblings contributed both borders to one box's width.

**Now:** a block-level box begins a line whatever its `white-space` is ([CSS 2.1
§9.4.1](https://www.w3.org/TR/CSS21/visuren.html#block-formatting)), so the float above is as wide as
its widest single line, matching a browser. Any shrink-to-fit box or auto table column around `nowrap`
block content is correspondingly narrower than it used to be — this is a width *reduction*, and a
layout that was accidentally relying on the extra room (an adjacent float that now fits beside it, a
table column that now shares its surplus differently) will reflow.

## An inline-level box sits on the line without `white-space: nowrap`

**Before:** every inline-level display — `inline-block`, `inline-flex`, `inline-table`, `inline-grid` —
was measured as opening a line of its own, so its width competed with the text beside it instead of
adding to it. An inherited `nowrap` happened to put it back on the line, which meant the correct width
was only reachable in a document that declared one:

```html
<div style="float:left">AB <span style="display:inline-block; width:50pt"></span></div>
```

measured 50pt — the box alone — where a browser measures 69.7852pt, and the float then drew over its
own content.

**Now:** an inline-level box never begins a line, so its width (and its explicit `width`) is added to
the line in progress. Such a box is measured the same with or without `white-space: nowrap`. Shrink-to-fit
boxes and auto table columns holding text next to an `inline-block`/`-flex`/`-table`/`-grid` are
correspondingly **wider** than they used to be.

An inline-level box that *follows* a block-level sibling still gets a line of its own — it is wrapped
in an anonymous block ([CSS 2.1
§9.2.1.1](https://www.w3.org/TR/CSS21/visuren.html#anonymous-block-level)), which is block-level — so
nothing changes for that shape.

A flex or grid **item** also still gets a line of its own whatever its own `display` says, because the
formatting context blockifies it ([css-display-3
§2.7](https://www.w3.org/TR/css-display-3/#blockify)). One shape here changes for the better: a
single-line flex **column** (or a one-column grid) whose items are `display: inline` used to be
measured as the sum of its items rather than the widest of them, so such a container was too wide.
It now matches the same container built from `<div>` items.
