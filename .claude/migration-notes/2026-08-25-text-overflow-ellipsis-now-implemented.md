# `text-overflow: ellipsis` is now implemented

**Landed:** 2026-08-25 — `text-overflow` had no `CssBox` wiring and was a complete no-op
**Doc section:** docs/html-css-support.md § [Text Layout](../../docs/html-css-support.md#text-layout)

`text-overflow` was previously a recognized CSS-OM property name only — it parsed and stored on the
style declaration, but had no `css-properties.json` entry, so it never reached `CssBox` and had zero
effect on layout or paint. A document setting `overflow: hidden; text-overflow: ellipsis` (Tailwind's
`truncate` utility, among others) got plain hard clipping with no `…` glyph, identical to `clip`.

`text-overflow: ellipsis` now truncates and appends `…` to whichever line box of a block container
actually overflows that container's content edge — this is not limited to a forced single-line
(`white-space: nowrap`) box, and it correctly finds the truncation edge for horizontal LTR/RTL and
vertical-rl/vertical-lr writing modes alike. It only takes effect on a container whose own `overflow`
is `hidden` (not `auto`/`scroll`, which don't establish a real clip in this non-interactive-PDF
renderer either — see the `overflow` row in docs/html-css-support.md).

A document that already set `text-overflow: ellipsis` expecting no visible effect (since it previously
had none) will now see truncated text with a trailing ellipsis wherever its content genuinely
overflows a `overflow: hidden` container. `text-overflow: clip` (the initial value) is unaffected —
clipping behavior is unchanged.

This change also fixed a related, previously-unnoticed bug it would otherwise have inherited: an
overflowing `direction: rtl` (or plain `text-align: right`) nowrap line stayed at its natural
(always left-to-right-flowing) position and spilled off the *physical right* edge instead of staying
flush-right and spilling off the physical left edge the way real browsers render it
(`CssLayoutEngine.ApplyRightAlignment`'s overflow guard, the horizontal analog of an earlier vertical
fix). A document with overflowing RTL or right-aligned nowrap content will now see it positioned
flush against the correct (right) edge, spilling left, rather than flush-left spilling right.
