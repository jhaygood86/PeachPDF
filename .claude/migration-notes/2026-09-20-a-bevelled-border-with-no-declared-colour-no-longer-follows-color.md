# A bevelled border with no declared colour no longer follows `color`

A border side styled `inset`, `outset`, `groove` or `ridge` whose colour is `currentcolor` — which is
`border-color`'s initial value, so this is every such border that names no colour — now shades a fixed
light grey, `rgb(238, 238, 238)`, instead of the element's own `color`. This matches Chromium, and it
is what an unstyled `<hr>` and a bare `border: 2px inset` have always looked like in a browser.

Measured, rendering the same document through v0.9.19 and through this build, against Chrome 153:

| declaration | v0.9.19 | now | Chrome 153 |
| --- | --- | --- | --- |
| `border: 20px inset` (default black text) | `#545454` / `#a8a8a8` | `#9a9a9a` / `#eeeeee` | `#9a9a9a` / `#eeeeee` |
| `border: 20px inset; color: red` | `#ab0000` / `#ff0000` | `#9a9a9a` / `#eeeeee` | `#9a9a9a` / `#eeeeee` |
| `border: 20px inset #808080` | `#2c2c2c` / `#d4d4d4` | *unchanged* | `#2c2c2c` / `#d4d4d4` |
| `outline: 20px inset; color: red` | `#ab0000` / `#ff0000` | *unchanged* | `#ab0000` / `#ff0000` |
| `<table style="border: 20px inset; color: red">` | `#ab0000` / `#ff0000` | *unchanged* | `#ab0000` / `#ff0000` |
| `<hr>` | `#9a9a9a` / `#eeeeee` | *unchanged* | `#9a9a9a` / `#eeeeee` |

## What changes for a document

- **A bevelled border that named no colour changes colour.** If it was picking up a deliberate
  `color`, it no longer does. To keep the old result, name the colour on the border:
  `border: 2px inset red` rather than `border: 2px inset` with `color: red`. Note this is not quite
  the identical byte for every colour — a declared colour shades from itself, so the two faces of a
  declared `#808080` are `#2c2c2c`/`#d4d4d4`, not the greys above.
- **A translucent or `transparent` `color` no longer makes such a border translucent.** The base is
  fully opaque.
- **`hr { border-style: dashed }` (or any flat style an author sets on a rule) is now the UA sheet's
  gray.** At v0.9.19 a rule ignored `border-style` altogether and painted the two greys flat; between
  then and now it began honouring the style but took the UA sheet's then-declared `#eee` as its
  colour, which was near-invisible on white. It resolves through `color` now, as in a browser.

## What does not change

`outline-color` and `column-rule-color` are not affected, even when their own style is bevelled —
only the four `border-*-color` longhands are. Neither is any element with a table `display` type
(`table`, `inline-table`, `table-row`, `table-cell` and the rest), which still shades its own
`currentcolor`. A default `<hr>` paints the same two colours as at v0.9.19 (its geometry did change
in the same release cycle, for unrelated reasons — see the sibling `<hr>` notes).

## `border-color: inherit` now resolves against the child's own `color`

A separate, smaller correction that rides along, because it is the same keyword being resolved in the
same place. `border-color` is not an inherited property, but `inherit` asks for the parent's computed
value — and that value is `currentcolor`, which each box resolves for itself.

```html
<div style="border: 20px solid; color: red">
  <div style="color: blue; border: 10px solid; border-color: inherit">child</div>
</div>
```

The child's border was **red** (the parent's resolved colour) and is now **blue** (its own), which is
what a browser paints. Documents that relied on the old behaviour were relying on a child ignoring its
own `color`; to get the parent's colour deliberately, name it rather than inheriting it.

## A logical `border-*-color: currentcolor` is no longer black

```html
<div style="color: red; border: 4pt solid; border-inline-start-color: currentcolor">…</div>
```

The left border was **black** and is now **red**. The four logical colour longhands
(`border-block-start-color`, `border-block-end-color`, `border-inline-start-color`,
`border-inline-end-color`) are mapped onto their physical edge *after* `currentcolor` was substituted,
so a logical longhand that named the keyword put it back unresolved and it fell through to black. It
is resolved per box at paint time now, so the order no longer matters. A logical longhand naming a
real colour (`border-inline-start-color: red`) was never affected.

## An `@page` margin box's border with no declared colour follows `color`

```css
@page { @bottom-center { content: "x"; color: red; border-bottom: 6pt solid } }
```

That border was **black** and is now **red**. An omitted colour slot in a `border`/`border-top`
shorthand carries the literal `initial` through the margin-box style path, which resolved as an
unparseable colour rather than as `border-*-color`'s actual initial value, `currentcolor`. The same
declaration on an ordinary element has always painted the text colour; a margin box now agrees with
it — and with the rule above, a *bevelled* margin-box edge with no declared colour shades the fixed
light base instead of black (`@page { border: 4pt inset }` was a shaded black, and is now
`#9a9a9a`/`#eeeeee`).

## Why

PeachPDF had no way to tell a border colour that came from `currentColor` apart from the same colour
declared, so it shaded whichever it was handed. Shading a bevel from the text colour has no usable
result at either end of the range — default black text gives a black-on-black frame — which is why
Blink substitutes a fixed base for exactly this case and every browser-rendered `inset` border looks
the way it does.
