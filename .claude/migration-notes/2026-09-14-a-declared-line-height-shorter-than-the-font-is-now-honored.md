# A declared `line-height` shorter than the font's own height is now honored

A line box used to be sized to **`max(line-height, the font's own height)`**. It is now sized to
`line-height` alone, per CSS 2.1
[§10.8](https://www.w3.org/TR/CSS21/visudet.html#line-height) — a non-replaced inline box contributes
exactly its own `line-height`, and glyphs taller than that overflow the line rather than growing it.

```html
<div style="font: 12px sans-serif; line-height: 12px">x</div>
```

was 13.8pt tall — the fallback font's own ascent+descent — and is now exactly 9pt (12px). The same
applies to every equivalent spelling: `line-height: 1`, `line-height: 1em`, and an explicit length.
`line-height: normal` is unaffected, and a `line-height` **taller** than the font behaved correctly
before and still does.

Two consequences a document author can see:

- **Text set with a tight `line-height` now occupies less vertical space.** A block of several such
  lines is now exactly `line count × line-height` tall, so documents that pack text tightly get
  shorter and may repaginate. Where the declared value was larger than the font's height — the common
  case for body text at `1.4`/`1.5` — nothing changes at all.
- **A background or border on such a block now paints over the line box, not over the glyphs.** The
  block's border box is its line boxes; content overflowing a short line box no longer grows the
  painted box. Descenders may now sit outside a background band that previously stretched to contain
  them.

A second, related change: every line box that holds content is now also at least as tall as the
**strut** (§10.8.1) — an imaginary inline box with the *block's* own font and `line-height`.

```html
<div style="font: 12px sans-serif; line-height: 24px"><span style="font: 2px/4px serif">x</span></div>
```

was 3pt tall — the inline child's `4px` line-height, the block's own never consulted — and is now
18pt (24px). A block whose line content all comes from a descendant declaring a shorter `line-height`
therefore keeps its own height instead of collapsing to the descendant's. An empty block is
unaffected: §9.4.2 keeps a line box holding no content at zero height.
