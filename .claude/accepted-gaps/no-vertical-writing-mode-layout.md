# `writing-mode` real layout is partial: inline content only, no block children, table/flex/columns unaffected

Tracking issue: [#547](https://github.com/jhaygood86/PeachPDF/issues/547).

CSS Writing Modes Level 4 defines `writing-mode: vertical-rl` / `vertical-lr` / `sideways-rl` /
`sideways-lr` as rotating which axis is inline and which is block for a box's whole layout: line boxes
stack along the block axis, glyphs orient per
[§text-orientation](https://www.w3.org/TR/css-writing-modes-4/#text-orientation), and both
[flex](https://www.w3.org/TR/css-flexbox-1/#writing-mode) and
[table](https://www.w3.org/TR/css-tables-3/) layout reinterpret their main/cross axes accordingly.

## What now works

`writing-mode`/`text-orientation` parse, cascade, and inherit correctly, and `writing-mode` correctly
drives CSS Logical Properties resolution (unchanged from before). Beyond that, a block box whose
`writing-mode` is `vertical-rl`/`vertical-lr` and whose content is inline-only (plain text and simple
nested inline elements — `DomUtils.ContainsInlinesOnly`, no block-level children) now gets real vertical
line flow: `CssBox.LayoutContents` dispatches such a box to
`CssLayoutEngine.CreateVerticalLineBoxes` instead of the ordinary horizontal `CreateLineBoxes`/`FlowBox`.
Lines ("columns") stack along the block axis (right-to-left for `vertical-rl`, left-to-right for
`vertical-lr`, via `WritingModeFrame`'s logical-to-physical conversion), text runs top-to-bottom within
each column, auto height shrinks to the content's own inline-axis extent, and glyphs paint rotated 90°
(`FragmentPainter.Text.cs`'s `SidewaysRotation`, reusing the `RGraphics.PushTransform`/`RMatrix` mechanism
already proven by `SvgRenderer.PaintGlyphs`). Such a box is treated as monolithic with respect to its
parent's own page fragmentation (`MonolithicContent.IsUnresumableOrthogonalFlow`) — it lays out its whole
subtree in one pass and is moved (not sliced) if it doesn't fit the current page, the same way a replaced
element is.

## What's still out of scope

- **Block children inside a vertical box** ([#760](https://github.com/jhaygood86/PeachPDF/issues/760)).
  `CreateVerticalLineBoxes` only runs for inline-only content; a vertical-writing-mode box containing a
  nested block element (or itself containing another vertical- or horizontal-writing-mode block child —
  orthogonal flow) still lays out as ordinary `horizontal-tb`.
- **Auto width is not content-driven** ([#761](https://github.com/jhaygood86/PeachPDF/issues/761)). An
  auto-width vertical box's width still comes from the ordinary (writing-mode-unaware) `GetBoxWidth`
  fill-available default, not from shrinking to the number of columns the content actually needs.
- **Floats, absolute positioning, hyphenation, bidi reordering, `text-align`, and
  `box-decoration-break: clone`** are not honored inside a vertical box's own content
  ([#768](https://github.com/jhaygood86/PeachPDF/issues/768)).
- **A nested inline element's own border/padding/margin does not reserve inline-axis (physical left/right,
  for `vertical-rl`/`vertical-lr`) space** ([#769](https://github.com/jhaygood86/PeachPDF/issues/769)), and
  its block-axis (physical top/bottom) padding/border is applied once per column it spans rather than once
  total — `CreateVerticalLineBoxes` never sets `CssBox.FirstHostingLineBox`/`LastHostingLineBox`, the
  bookkeeping `CssLineBox.UpdateRectangle` needs to gate leading/trailing inset correctly, so a
  bordered/padded `<span>` inside vertical text paints with the wrong decoration box.
- **An explicit `writing-mode` override on a non-atomic nested inline element** (e.g. a `<span>`, not an
  `inline-block`/`inline-table`) is laid out using its containing block's writing-mode (correct — a plain
  inline never establishes its own flow) but may paint using its own, different, cascaded `WritingMode`
  value, producing a rotation mismatch. Per CSS Writing Modes, `writing-mode` has no defined effect on a
  non-atomic inline in the first place, so this is a narrow, spec-consistent-to-ignore edge case rather
  than a behavior authors should rely on either way.
- **Real per-character `text-orientation`** ([#765](https://github.com/jhaygood86/PeachPDF/issues/765)).
  Every glyph currently paints rotated 90° regardless of `text-orientation`'s value (equivalent to
  `sideways` always) — `mixed`'s real per-character upright/rotated split (Unicode's Vertical_Orientation
  property) is not implemented.
- **`sideways-rl`/`sideways-lr`** ([#766](https://github.com/jhaygood86/PeachPDF/issues/766)) still render
  as `horizontal-tb` throughout (`WritingModeFrame.IsVertical` is true only for `vertical-rl`/`vertical-lr`).
- **Table** ([#762](https://github.com/jhaygood86/PeachPDF/issues/762)), **Flexbox**
  ([#763](https://github.com/jhaygood86/PeachPDF/issues/763)), and **Multi-column**
  ([#764](https://github.com/jhaygood86/PeachPDF/issues/764)) layout engines don't read `writing-mode` at
  all — a vertical-writing-mode table/flex/multicol container still lays out its own rows/columns/items as
  `horizontal-tb`.
- **A vertical box's own content never fragments across a page boundary**
  ([#767](https://github.com/jhaygood86/PeachPDF/issues/767)) — being monolithic, it is moved whole to the
  next page if it doesn't fit the current one, or displaced-per-band (never resized) if it fits nowhere; it
  cannot yet split its own content the way ordinary horizontal flow does.
- **True OpenType vertical metrics (`vhea`/`vmtx`/`VORG`) are parsed but not yet consulted**
  ([#770](https://github.com/jhaygood86/PeachPDF/issues/770)) by layout or paint — glyph advance/positioning
  under vertical writing modes still uses the same horizontal-advance metrics as `horizontal-tb`, just
  reinterpreted geometrically (rotated), not real vertical typesetting metrics.

The reader-facing note is in `docs/html-css-support.md`'s `writing-mode`/`text-orientation` rows.
