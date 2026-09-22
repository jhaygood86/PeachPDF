# An `<hr>` is an ordinary block: borderless rules have no height, `min-width`/`max-width` apply, and it shrinks to fit like a block

The `<hr>` layout class is gone; a rule now takes the same layout path as an equivalent `<div>`. Three
changes a document author could see, each new relative to v0.9.19 (where `CssBoxHr.PerformLayoutImp` still
carried a hard-coded `height = 2`, resolved its own width, and never read `min-width`/`max-width`).

## A rule with no border, padding or height is 0 tall

`hr { border: none }` (or `border: 0`) used to leave a nominal 2-unit box. A browser, and an empty
`<div>`, give it 0. The flow advance was already 0, so the following block did not move, but a container
holding only the rule took the 2 units as its own height - so a wrapper with a background, border or outline
around a reset rule was 2pt taller than it should be. It is now exactly as tall as a browser draws it.
`hr { border: none; height: 1px; background: #ddd }`, the common modern reset, is unaffected: the declared height wins.

## `min-width` and `max-width` apply

They were ignored on a rule. `hr { width: 50%; min-width: 180pt }` in a 200pt block was 100pt wide and is now
180pt, and `max-width` clamps it the same way ([CSS 2.1 §10.4](https://www.w3.org/TR/CSS21/visudet.html#min-max-widths)).

## An auto-width rule shrinks to fit where a block would

A rule used to fill its container's width whatever its `display` or position. As a float, an absolutely
positioned box, a flex item or an `inline-block` it now takes its content width - which is nothing - plus its
own borders, 1.5pt for the default rule, as in a browser. A declared `width` still wins, and an in-flow rule
with `width: auto` still fills its container.
