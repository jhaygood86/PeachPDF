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
<hr style="border: 0.5pt dotted">  <!-- v0.9.19: 1px solid.            Now: 0.5pt dotted. -->
```

An author who was relying on the rewrite to get a visible rule out of `border: none` needs to declare
the border they want. The rule's height is unaffected either way — only what is painted into it.

The default `<hr>` (no author style) keeps the same 1px width and the same total height it always
had, but now shades as the `inset` the UA stylesheet declares rather than as `solid`, so its top edge
darkens:

| edge | v0.9.19 | now |
| --- | --- | --- |
| top | `#9a9a9a` | `#464646` |
| bottom | `#eeeeee` | `#eeeeee` (too light to lighten, so the declared color stands) |

The default rule therefore reads as a higher-contrast engraved line than it did.

## `<hr noshade>` and `<hr color=…>` stay flat

The UA stylesheet gained the HTML Standard's companion rule to `hr { border-style: inset }`:

```css
hr[color], hr[noshade] { border-style: solid }
```

Either presentational attribute makes the rule flat rather than engraved, which is the whole point of
`noshade`. In v0.9.19 every rule was flat regardless, so this changes nothing an author would see for
those two attributes — it is what keeps them right now that `inset` does something.

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

## Why

`border-style` was being discarded on `<hr>` and only on `<hr>`, which made it the one box in the
document whose border did not follow [CSS 2.1
§8.5.3](https://www.w3.org/TR/CSS21/box.html#border-style-properties). A zero-height `<div>` carrying
the identical border already painted correctly; the rule now emits exactly the same draw calls it
does.
