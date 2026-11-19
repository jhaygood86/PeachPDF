# Radial gradients whose start point lies on the end circle's edge

`radial_shading_point_at_border` (PDFium corpus) is an extended radial shading whose start circle is a point on the edge of the end circle. PDF
8.7.4.5.4 then paints the whole half-plane the cone of circles sweeps out; the raster canvas's focal radial brush paints only the circle and clamps
the focus inside it, so the cone is missing (12% of that page differs). The PDF writer is not affected: it writes the shading with its /Extend and the
viewer draws it. A two-circle gradient with cone support is a change to `RasterCanvas`'s brush shared with CSS radial gradients (which never have
this case), for one corpus page; deferred. Not tracked as a spec deviation of a published feature.
