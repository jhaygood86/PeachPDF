# outline-style: auto ignores outline-width

Up to and including v0.9.18, `outline-style: auto` was drawn as a solid ring at whatever
`outline-width` declared, seated entirely outside the edge `outline-offset` puts it at — the same
rendering as `outline-style: solid` in every respect. `outline: 8px auto` painted an 8px ring.

It is now drawn as a solid ring of a fixed 2px, whatever `outline-width` says, and that ring is
*centred* on the edge `outline-offset` puts it at rather than sitting wholly outside it. With the
default zero offset it straddles the border edge, 1px either side. `outline: 8px auto`,
`outline: 1px auto`, `outline: 20px auto`, `outline: 0 auto` and a bare `outline-style: auto` now all
paint exactly the same ring.

This is what CSS Basic User Interface 4 §4 requires: *"The `outline-width` property is ignored when
`outline-style` is `auto`."* The neighbouring sentence, *"User agents may treat `auto` as `solid`"*,
is a choice about the style and not a licence to keep the author's width — so `auto` is still solid,
but at the UA's own width. The width and the centring both match what Chrome draws.

A document that used `outline-style: auto` to get an outline of a particular thickness will now show a
thin 2px ring instead. **Use any other `outline-style` to control the width** — `solid` reproduces the
old rendering exactly:

```css
/* before: an 8px ring; now: a 2px UA ring */
outline: 8px auto #4a90d9;

/* the old rendering, unchanged and still fully width-controlled */
outline: 8px solid #4a90d9;
```

`outline-color` still applies to `auto` as it always did, and an `auto` outline remains layout-neutral
and rectangular.
