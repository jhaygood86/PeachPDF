# A tall absolutely positioned box is sliced, not fragmented

_CSS Fragmentation Level 3 §4.4 (content that is sliced rather than broken should not lose lines), and
CSS Positioned Layout 3 (an absolutely positioned box fragments within its containing block). Tracker:
[#1372](https://github.com/jhaygood86/PeachPDF/issues/1372). The cut line is the same slice-boundary
mechanism as [#1328](https://github.com/jhaygood86/PeachPDF/issues/1328)._

A `position: absolute` box in block flow is laid out unbroken (`CssBox.LayoutBlockChildUnbroken`): the
fragmentainer is detached and word page breaks are suppressed. One taller than the page runs on across
pages and each page draws its own slice. A box that is or holds a multi-column container keeps the breaking
path, because the columns engine needs the fragmentainer: laid out unbroken, 20 paragraphs in `columns: 2`
after an in-flow paragraph lost W18–W20 (a review of #1334). As its block's first child it loses them on
`main` too, tracked as #1376. Two consequences:

- A line that straddles a page boundary is cut, half drawn at the foot of one page and half at the head
  of the next. No word is lost from the PDF text, but the line is not readable on either page. A probe
  with 37 lines on a 300×200pt page cut W17 and W30 (seen on PDFium rasters).
- Nothing inside the box is relocated by §4.3: an image or a `break-inside: avoid` block is sliced, and a
  table inside it neither repeats its `<thead>` nor breaks between rows.
- A forced break inside the box (`break-before: page` on one of its children) is not taken, because the
  fragmentainer is detached: `<div style="position:absolute"><section>A</section><section
  style="break-before:page">B</section></div>` runs B on straight after A, where the breaking path started a
  new page. css-break-3 §3.1 requires break properties only in the fragmentation root's own flow, but the
  old path did honour it.

Before this change (#1349), such a box broke between its lines instead. That was worse: its break ended the
pass, the next pass resumed inside the box on the following page, and every in-flow box after it in the
same block was placed back on the page the break left, already emitted, so it was drawn on no page (all
ten paragraphs after a 30-line box). Fragmenting it properly needs the box's breaks to be resumed
independently of the in-flow token chain, which the driver cannot do today. `position: fixed` is not
affected: the emitter draws a fixed box on every page itself.
