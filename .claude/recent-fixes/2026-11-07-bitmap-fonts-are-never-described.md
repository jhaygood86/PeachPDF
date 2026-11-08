# A bitmap-only system font is never registered as an installed face

**Symptom.** `FontSetPublicApiTests.MatchOrFallback_ForAnUnknownFamily_StillAnswersWithSomeFace` failed on the
`macos-latest` engine leg only, intermittently: `NotSupportedException: Bitmap fonts are not supported.` from
`OpenTypeFontface.Read()`.

**Cause.** `FontResolver.ResolveFace`'s last resort for an unknown family is `InstalledFonts.First()`, an arbitrary face.
`TtfFontDescription.LoadDescription` read only the `name`/`OS/2`/`fvar` tables, so a font carrying a `bhed` table
(Apple's bitmap-only `head`) was registered like any other; when it happened to be first, loading it for drawing threw.
Which font was first depended on the runner image, so the same commit passed and failed.

**Fix.** The table-directory pass records `bhed` and `LoadDescription` throws `InvalidDataException`. Every caller already
treats that as "skip this face" (`LoadDescriptions` for a collection, the `catch` around the installed-font scan for a
single file), so such a font is not installed and cannot be picked. `OpenTypeFontface` keeps its own refusal for bytes
handed in directly.

**Evidence.** New `Description_RejectsABitmapFontSoItIsNeverInstalled`; the whole `PeachDrawing.Text.Tests` project passes in Release.
