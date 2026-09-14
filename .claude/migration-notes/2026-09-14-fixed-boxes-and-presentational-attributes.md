# A `position: fixed` box sits inside the page margins, and an author rule beats a `width=` attribute

Two related corrections to where content lands. Both change rendered output for documents that already
worked, so both are worth calling out in release notes.

## `position: fixed` is positioned against the page area, not the sheet

**Before:** a fixed box's offsets were measured from the **sheet corner** — `top: 0; left: 0` put the box
in the top-left corner of the paper, on the `@page` margin. This was documented as "renders ignoring page
margins". Its *sizes* did not agree: a percentage `width`/`height`, and the `left: 0; right: 0` fill, all
resolved against the page **area** (the sheet minus its margins). So a `left: 0; right: 0` rule was given
the content width but drawn from the sheet edge, ending one margin short of the right content edge.

**Now:** the containing block is the page area, as
[CSS 2.1 §10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details) specifies for paged
media ("the viewport in the case of continuous media or the page area in the case of paged media") — the
same place a browser puts it when printing. `top: 0; left: 0` is the top-left of the content area;
`left: 0; right: 0` spans the measure exactly.

**What a document author sees:** every fixed box moves inward by the page margins. A running header
written as `position: fixed; top: 0` now starts at the top content edge rather than the paper edge; to
put it back on the margin, give it a negative offset (`top: -38px` for a 38px top margin) or set the
page margin to 0. On a document whose pages have different margins or sizes (`@page :first`, a named
page, mixed orientation), a fixed box now follows each page's own area instead of holding one absolute
position on every sheet — an absolute-length offset that used to land identically on every page now
lands at the same offset *into each page's own area*.

## Presentational attributes no longer beat author style rules

**Before:** `width`, `height`, `bgcolor`, `align`, `border`, `color`, `face`, `nowrap` and the rest of
HTML's presentational attributes were applied *after* the author stylesheet, so they outranked every
author rule. `<svg width="500" height="110">` kept its 500×110 even under a `.logo { width: 365px }`
rule; only an inline `style="..."` could override it.

**Now:** they are applied between the UA sheet and the author sheets, where HTML puts them — declarations
at the very start of the author origin with zero specificity — so **any** author rule beats them, as in
every browser. `revert` in an author rule still rolls back past them to the UA value.

**What a document author sees:** a stylesheet rule that was previously being silently ignored now takes
effect. The common case is a sized `<svg>`, `<img>`, `<table>` or `<td>` whose CSS rule now wins — usually
the intended result, and the same one a browser was already showing.
