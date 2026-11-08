# Table bug batch (cell alignment measure, rowspan clamp, quirks mode, float avoidance)

Issues: #2255, #2259, #2260, #2258 (one root cause), #2292, #2195, #2141, #2187 (fits case), #2186 (box centring part), #124 (already fixed, verified).

- **Cell alignment** (`CssLayoutEngine.ApplyCellVerticalAlignment`): the content end is `CellContentBottom` alone (last in-flow block's margin edge, floats, own lines). The old `max(word-rect measure, CellContentBottom)` let the words' rectangles win, and they carry a relative offset and miss a negative margin. `GetMaximumBottom` also skips `display: none` boxes. `ResolveMaxHeight` and the two other max-height sites return nothing for a table cell (CSS 2.1 sections 10.7, 17.5.3; browsers ignore it).
- **rowspan clamp**: `GetEffectiveEndRowIndex` already clamped; the lost cell came from consumers using the raw attribute (no row ever closed the spanning cell). `GetRowSpan` now clamps body/footer spans to the rows left in the group; a header's span is left alone because one crossing into the body is deliberate (#788).
- **Quirks mode**: `HtmlParser` decides quirks from the DOCTYPE (HTML 13.2.6.4.1, full-quirks only) at the first non-comment, non-whitespace token. Trap: the decision must run once; evaluating it on every token while the phase is still Initial turned a standards DOCTYPE into quirks on the next token.
- **Column axis floor**: `ColumnAxisFloor` is no longer 0 for horizontal tables.
- **Float avoidance**: `PlaceBesideFloats` reuses `ExtentBesideFloats` (multicol precedent). Only the fits-beside, left-float case; see the accepted gap.
- **Not done**: #2300 (multi-column hang: a stack dump shows the CPU in `CssLayoutEngineColumns.FillColumns`/`BoxGeometrySnapshot.CaptureBox`, not traced further), #2220 (the float is at the cell's top in layout; the 1.5px may come from paint or Chrome's line box), #2196, #1946, and the inheritance half of #2186.
