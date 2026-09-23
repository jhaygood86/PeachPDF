# Positioned floats paint in Appendix E step 8, not step 5 (#1279)

**Symptom:** a real page's category sidebar (`#contentleft { float: left; position: relative }`
inside `#contentcontainer { position: relative; background: #fff }`) was invisible. The text was in
the PDF (pdfium text extraction found it), but MuPDF's `get_texttrace`/`get_drawings` sequence numbers
showed the sidebar text drawn *before* `#contentcontainer`'s white background fill.

**Cause:** `FragmentPainter`'s stacking loop painted `p.Box.IsFloated` in the float pass, with no
`!IsPositioned` guard. The positioned pass also took it (`IsPositioned`), but `_painted` dedup kept the
first, early paint. So the float painted before its own positioned parent, which is itself a step-8
participant of the same stacking context, and got covered. Minimized: a positioned float inside a
positioned opaque parent (`positioned float` + `positioned parent` are both required; either alone paints fine).

**Fix:** the float pass is `IsFloated && !IsPositioned`, matching Appendix E step 5 ("non-positioned
floats") and the block/inline passes' existing `!IsPositioned` guards. `StackingOrder` discovery is
unchanged.

**Evidence:** two new `StackingContextOrderingTests` (positioned float after positioned parent's
background; positioned float after a later plain float sibling) fail before and pass after. The full
net8.0 suite is green (13298 passed). The page re-rasterized in PDFium and MuPDF shows the full
category list.
