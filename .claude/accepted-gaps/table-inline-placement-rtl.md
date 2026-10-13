# A `<table>` does not end-align in an rtl container (#1251)

The single-auto-margin part of this gap is closed: `CssLayoutEngineTable` passes its settled table width to
`CssLayoutEngine.GetActualMarginLeft/Right`, so a lone auto margin now receives the remaining space. The
integration tests cover left-auto, right-auto, and an over-wide table, including the table's final position.

The remaining spec deviation is a narrow table in an `rtl` containing block. It still sits at the left edge
instead of aligning to the right-hand inline-start edge, contrary to the direction-dependent horizontal
resolution in [CSS 2.1 §10.3.3](https://www.w3.org/TR/CSS21/visudet.html#blockwidth). Chrome 153 vs PeachPDF,
in pt within a 200pt container:

| declaration | Chrome | PeachPDF |
| --- | --- | --- |
| `direction: rtl`, `width: 100pt` | 100..200 | 0..100 |

The table layout engine still anchors the table from the physical left edge. Resolving an auto margin from the
settled width fixes the single-auto case in LTR, but does not make the table's initial inline placement
writing-direction-aware. The remaining behavior is tracked as a spec deviation in #1251; this file can be
deleted when RTL table placement is corrected and the limitation sentence is removed from
`docs/html-css-support.md`.
