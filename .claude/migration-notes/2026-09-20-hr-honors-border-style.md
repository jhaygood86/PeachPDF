# `<hr>` honors `border-style`

An `<hr>` paints its rule through the ordinary border path now, so `border-style` applies to it the
same way it applies to every other box. In v0.9.19 the rule was painted by a path of its own that
filled each side with the declared color flat, and read `border-style` nowhere at all.

## What changes

Every non-`solid` style on an `<hr>` renders differently than it did:

```html
<hr style="border: 2px inset #808080">
```

| | v0.9.19 | now | Chrome |
| --- | --- | --- | --- |
| top/left face | `#808080` | `#2c2c2c` | `#2c2c2c` |
| bottom/right face | `#808080` | `#d4d4d4` | `#d4d4d4` |

The same for `outset`, `groove` and `ridge` (which shade their faces), `dotted` and `dashed` (which
fit a pattern to the edge instead of painting a continuous band), and `double` (which draws its two
lines with a gap rather than one filled slab). `solid` is unchanged.

## `border-style: none` on a rule now paints nothing

Layout used to rewrite a thin rule's computed style, forcing `border-top-style`/`border-bottom-style`
to `solid` and both widths to `1px` whenever the rule was two units or shorter. That rewrite is gone,
so:

```html
<hr style="border: none">          <!-- v0.9.19: two 1px solid lines.  Now: nothing. -->
<hr style="border: 0">             <!-- v0.9.19: two 1px solid lines.  Now: nothing. -->
<hr style="border: 0.5pt dotted">  <!-- v0.9.19: 1px solid.            Now: 0.5pt dotted. -->
```

An author who was relying on the rewrite to get a visible rule out of `border: none` needs to declare
the border they want. The rule's height is unaffected either way — only what is painted into it.

**A default `<hr>` is unchanged.** It still paints `#9a9a9a` over `#eeeeee`, at the same width and
height, wherever it appears. Those two greys used to be declared per side in the UA stylesheet — they
are what Chrome's bevel *produces*, written down as literals — and a rule that now bevels for real
would have shaded them a second time (`#9a9a9a` → `#464646`). The sheet declares the single base
colour they derive from instead, `border: 1px inset #eee`, so the output is byte-identical.

## `<hr noshade>` and `<hr color=…>` stay flat

The UA stylesheet gained the HTML Standard's companion rule to `hr { border-style: inset }`:

```css
hr[color], hr[noshade] { border-style: solid }
```

Either presentational attribute makes the rule flat rather than engraved, which is the whole point of
`noshade`, and the flat rule carries the spec's own `color: gray`:

| | v0.9.19 | now | Chrome |
| --- | --- | --- | --- |
| `<hr noshade>` | `#9a9a9a` / `#eeeeee` | `#808080` flat | `#808080` flat |
| `<hr color="red">` | `#9a9a9a` / `#eeeeee` | `#808080` flat | red, flat |

`<hr noshade>` now matches a browser exactly. `<hr color>` gets the right *shape* but still not the
colour, because the `color` attribute is not yet wired up as a presentational hint for the `color`
property — that is unchanged from v0.9.19, which did not honour it either.

## A valueless HTML attribute is now visible to `[attr]` selectors

`<hr noshade>` only reaches the rule above because of a second fix, which is user-visible on its own:
an attribute written without a value (`<input disabled>`, `<p hidden>`, `<details open>`) was stored
with no value rather than the empty string the HTML Standard gives it, so an `[attr]` presence
selector never matched it. The identical attribute written as `disabled=""` did match.

```html
<style>[disabled] { opacity: 0.5 }</style>
<input disabled>   <!-- v0.9.19: unstyled.  Now: matched. -->
```

So a rule that was silently doing nothing may start applying. Nothing that already matched stops
matching: an absent attribute still does not match, and `attr()` already substituted the empty string
for a valueless attribute.

Two further consequences, both of them the spec's own answer rather than a judgement call:

- **`<option value>`** exports the empty string in an interactive form, instead of falling back to the
  option's label text. Per the HTML Standard an `option` with a `value` attribute takes that attribute
  as its value, and a valueless attribute's value is `""`; the label fallback applies only when the
  attribute is *absent*.
- **`<mfenced open>`** renders with no opening fence, instead of the default `(`. An empty
  `open`/`close` attribute omits that fence, and a valueless one is empty.

## Why

`border-style` was being discarded on `<hr>` and only on `<hr>`, which made it the one box in the
document whose border did not follow [CSS 2.1
§8.5.3](https://www.w3.org/TR/CSS21/box.html#border-style-properties). A zero-height `<div>` carrying
the identical border already painted correctly; the rule now emits exactly the same draw calls it
does.
