# `width` on a `display: inline-block` now occupies that width on the line

An atomic inline-level box occupies its own used width on the line (CSS 2.1 §10.3.9). The inline
flow accumulated its content's word widths instead, so a `width` on a `display: inline-block` had no
effect at all and whatever followed sat flush against the box's text.

Two shapes it broke, both common:

- **A fixed-width label.** `<span style="display:inline-block;width:160px">Hi</span>` reserved only
  the width of "Hi", so a column of such labels stopped lining its values up.
- **An empty bordered box used as a glyph** — a checkbox drawn as
  `<span style="display:inline-block;width:120px;border:1px solid"></span>` — has no content at all,
  so it took no room whatsoever.

Such a box now reserves its declared width. Measured against Chrome 152 on the same shapes: the
first puts the following text exactly 120pt past the container's edge (160px at 96 CSS dpi), the
second at 91.5pt (90pt plus its 1px borders). PeachPDF now agrees with both to within 0.1pt.

Content **wider** than the declared width still overflows rather than being pulled back, which is
what `overflow: visible` means, and a **percentage** width is unaffected.

See [Display](../../docs/html-css-support.md#display) in `docs/html-css-support.md`.
