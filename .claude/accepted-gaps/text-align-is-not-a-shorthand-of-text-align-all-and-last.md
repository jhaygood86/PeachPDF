# `text-align` is a longhand here, not a shorthand over `text-align-all`/`text-align-last`

Tracked as **#1027**.

Left behind by the #1021 work, which implemented `text-align-last`
(see [../recent-fixes/2026-09-13-justify-ends-a-paragraph-at-a-forced-break.md](../recent-fixes/2026-09-13-justify-ends-a-paragraph-at-a-forced-break.md)).

[css-text-3 §6.1](https://www.w3.org/TR/css-text-3/#text-align-property) defines `text-align` as a
**shorthand** for `text-align-all` (§6.2) and `text-align-last` (§6.3): any value other than
`match-parent`/`justify-all` sets `text-align-all` to it **and resets `text-align-last` to `auto`**,
while `justify-all` sets both to `justify`.

PeachPDF instead has two independent inherited longhands: `text-align` (playing the role of
`text-align-all`) and `text-align-last`. `text-align-all`, `justify-all` and `match-parent` are not
recognized at all.

The one reachable divergence is the shorthand's reset. Given

```html
<div style="text-align-last: justify">
  <p style="text-align: center">…</p>
</div>
```

the spec makes `p`'s last line **centred** (`text-align: center` reset `text-align-last` to `auto`),
while PeachPDF justifies it (the inherited `text-align-last: justify` is never reset). Declaration
order within one block matters to the spec for the same reason and does not here.

## Why it was left

Every shipping browser behaves the way PeachPDF now does. Chromium, Gecko and WebKit all ship
`text-align-last` as an independent longhand and **none of them implements `text-align-all`**, so the
reset does not happen in a browser either — implementing the shorthand faithfully would make PeachPDF
diverge from every renderer an author actually checks their document against, in exchange for matching
prose no engine follows.

It is also not a cheap change here. `text-align` would have to become a real `ShorthandProperty` in
the CSS-OM (`PropertyFactory.AddShorthand`), which changes what `StyleDeclaration.TextAlign` returns —
`MarginBoxRenderer.ResolveAlignment` reads exactly that for `@page` margin-box alignment — and the
render-side registry entry would have to move from `text-align` to a new `text-align-all`, touching the
presentational-attribute path (`DomParser`'s `align=` handling writes `box.TextAlign` directly) and the
UA stylesheet (`th`/`caption`/`center`).

Revisit if a browser ever ships `text-align-all`, or if a real document turns up that depends on the
reset.
