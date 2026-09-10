# Selectable color-font CIDs must have a non-empty `glyf` outline

PDFium drops a shown Type 0/CID glyph from its text page when that CID's embedded TrueType `glyf`
entry has zero contours. `3 Tr`, `/ToUnicode`, and a surrounding `/ActualText` span do not change that:
the operator is present and MuPDF extracts it, but Chrome/Edge cannot select, search, or copy it.

COLR base glyphs are commonly empty by design; their visible contours belong to layer glyphs referenced
from the COLR paint graph. For every selected empty COLR base CID, `OpenTypeFontface.CreateFontSubSet`
therefore substitutes a stand-in contour in the PDF's embedded subset. It is safe only because the
subset is used exclusively by the color path's rendering-mode-3 text; the actual artwork remains the
native PDF vectors emitted from the original font.

**Size that contour to the glyph's real box** - its own advance width by the font's ascent/descent, as
`BuildInvisibleSelectionGlyph` does - not to a token rectangle. PDFium derives a character's selection
box from the contour extents, so a 1x1-font-unit stand-in still extracts and still answers Ctrl+F, but
reports a 0.04pt character box against a 42pt emoji: the search highlight is invisible and a mouse drag
has essentially no hit target. Measured on a 7-emoji page, 1x1 units gave 0.042 x 0.042 pt boxes while a
glyph-sized contour gave 52.3 x 49.8 pt, with both PDFium and MuPDF rasterizing byte-identical output -
mode 3 suppresses the larger contour exactly as it suppresses the smaller one.

Do not replace this with COLR layer closure. A text-showing operator references the base CID, never its
layer CIDs, so embedding the layers does not stop PDFium dropping the shown glyph and only bloats the
subset. Verify changes through an actual PDFium text page as well as MuPDF extraction, and rasterize with
both engines to confirm the synthetic contour produces no ink.
