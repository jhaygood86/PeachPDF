# Shading /Extend and the sh operator's space

Found by the PDFium corpus (`axial_/radial_shading_point_at_border_no_extend`, `radial_shading_point_at_center`; `shade` and
`shade-tensor` improved from 67%/35% of pixels differing to 14%/9%).

Two separate defects:
- **`/Extend` was never read.** A gradient brush pads its end colors forever, so a shading that must stop paints the whole page.
  `PdfShadingBrushFactory.CreateBrush` now also returns a `region` (a polygon in current space) and the interpreter clips to it: for
  `sh`, around the big rectangle; for a pattern fill, around the path. Axial: the band between the perpendiculars at the ends
  (their directions are carried over by `toCurrent`, so an uneven scale keeps them right angles in shading space), open where it extends.
  Radial: the convex hull of the first and last circle (sampled, 128 points each), with an extended side reaching the cone's apex when
  the radius falls that way. An extension that grows without limit is left unclipped on purpose.
- **`sh` mapped its shading through the inverse CTM, as a pattern's shading is.** The shading space of `sh` is the current user space
  (PDF 8.7.4.2), so under `cm` the gradient and every mesh (`DrawMesh`) was laid out at the wrong scale; unclipped extension hid it for
  axial and radial. `ShadingToCurrent(.., inUserSpace)` now makes `sh` identity and keeps the pattern path unchanged.

Trap: the first version of the clip drew a 1 px strip, which is what the second bug looks like once the first is fixed.

Not done (still failing in the corpus): `radial_shading_point_at_border` (the extended cone is not painted: a radial brush limit in the
drawing backends), `shade` column 4 (a Lab color space reached through a function draws black), `shade-tensor` (patches with shared edges,
a handful of colors), `bug_42271010` (function type 0 sampling). Checked against PDFium's own expected images only; MuPDF was not run
because the corpus PDFs are templates and the expanded files are not kept.
