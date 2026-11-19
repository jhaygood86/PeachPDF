# PDF import: negative Tf with -100 Tz, and Quartz symbolic TrueType subsets

Two real PDFs drew wrong in PeachReader.

**Mirrored text (report-writer PDFs).** A top-left coordinate system is written as a flipped CTM, `-100 Tz` and negative `Tf`
sizes. PDF 9.4.4 scales a glyph by `Tfs*Th` across and advances the pen by `(w*Tfs + Tc + Tw)*Th`: shapes follow the sign of
`Tfs*Th`, the pen the sign of `Th`. `ShowText` flipped y for a negative size but left the shape's x scale at `Th`, so every glyph
was mirrored about its own origin (pen positions were right, which is why it read as reversed letters with odd gaps). Now glyph
positions carry `sign(Tfs)` and the run's x scale is `Th*sign(Tfs)`; the product is the pen's own. Rise follows the same sign.
Left alone: the text-reading path (uses positions only) and Type3 (already multiplies the signed size). Vertical text still
uses the old expression (no real file seen).

**No text at all (macOS Quartz output).** Subsets keep a cmap of only Macintosh (1,0) format 6, and a name table with only the
PostScript name on the Mac platform. The font loader reads neither (it accepts Windows/Unicode cmaps, and needs a Windows
family and full name): load failed with `faceName` null, and the whole page lost its text. `SfntRepair.AddMissingTables` now
rewrites a Mac-only cmap as a Windows format 4 table at `U+F000 + code` and replaces a name table that lacks Windows family and
full names. `PdfImportedFont` reads a symbolic TrueType font with no `/Encoding` by `U+F000 + code` first (PDF 9.6.5.4), so a
code never resolves through whatever its ToUnicode text happens to be in the cmap.

Trap: the name table existed, so "only adds missing tables" did not cover it; the crash was in the name, not the cmap.

Evidence: both files rendered before/after through a throwaway `RasterCanvas` harness (mirrored text, then a full page with
logo, signature and bullet); `PdfTextOperatorTests.NegativeSizeWithNegativeTz_*` fails without the fix; `SfntRepairTests`.
