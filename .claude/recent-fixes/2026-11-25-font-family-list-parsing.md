# font-family list parsing (quoted names in margin boxes)

Issue 2455: `font-family: "Lato"` in an `@page` margin box fell back to the default font; `Lato` worked.

**Cause.** `FontFamilyResolver.Resolve` had a single-family shortcut that handed the raw declaration text (quotes included) to `RenderContext.GetFont`. The multi-family loop, the codepoint overload, `HtmlContainerInt.FamilyNamesOf` and `CssValueParser.GetFontFamilyByName` each hand-rolled their own `Split(',')` + `Trim('"','\'')`, which also got commas-in-strings, escapes, generic-keyword quoting and whitespace collapse wrong.

**Fix.** One parser, `Html/Core/Utils/FontFamilyList.cs`, over the CSS tokenizer (it already decodes string escapes and keeps a comma inside a `String` token): split on `Comma` tokens, one `String` token is a quoted family, a run of idents is one unquoted name joined with single spaces. Every site above now goes through it. The CSS-OM `FontFamiliesConverter` only validates/serializes (opaque `IPropertyValue`), so it could not be reused for the names.

**Quoted generics.** A quoted `"serif"` is a family named serif, not the generic: the resolver skips it unless `RenderContext.IsRegisteredFontFamily` (new public API; registered names only, unlike `IsFontExists` which also counts `AddFontFamilyMapping`) says a family of that exact name exists.

**Traps.** A bare digit in an unquoted name (`Source Sans 3`) is a Number token, so the CSS-OM rejects the whole declaration (browsers do too); tests must quote such names. A lone trailing `\` is a stray delimiter, also invalid.

**Evidence.** `FontFamilyListTests`, `MarginBoxRendererFontTests` (quoted/escaped/comma/generic cases), `MarginBoxQuotedFontFamilyTests` (issue repro: the registered face's name only reaches the PDF when the margin box resolved it).
