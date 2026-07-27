# Removed the dead `idName`/`fontData` font-realization chain

_Landed 2026-07-21._

**Removed the dead `idName`/`fontData` font-realization chain**: the `OpenTypeFontface(byte[], string)` ctor was broken (NRE — its `FontSource` init was commented out) and sat under a fully dead, `Debug.Assert(false)`-gated path (`IContentStream.GetFontName(string, byte[], …)` → `PdfFontTable.GetFont(string, byte[])` → `PdfType0Font`/`PdfCIDFont` byte[] ctors → `FontDescriptorCache.GetOrCreateDescriptor(idName, fontData)` → `OpenTypeDescriptor(key, idName, fontData)` → the broken ctor). None of it was reachable (all 4369 tests run in Debug and never trip the assert). Removed the whole island across 9 files; `TryGetFontName(string)` (which uses the working `FontTable.TryGetFont`) was left alone.
