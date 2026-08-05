# `<font face="...">` no longer crashes layout, and now applies the font

**Landed:** 2026-08-05
**Doc section:** none — `html-css-support.md` never documented `face` (it was silently broken), so there is no existing callout to update.
**Verified against v0.9.8:** the `NotImplementedException` throw is present verbatim at `v0.9.8` (`DomParser.cs`'s `HtmlConstants.Face` case) — this is a genuine change since the last release, not a pre-existing fix that only reads as new.

Any document containing the legacy `<font face="...">` attribute previously threw an uncaught `NotImplementedException` during layout, regardless of the attribute's value. `face` now resolves against installed font families the same way the CSS `font-family` property does (`CssValueParser.GetFontFamilyByName`): the first comma-separated candidate that's actually installed is applied, or the document's default font is used if none of them are — never a crash.
