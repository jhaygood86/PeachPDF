# Relative `@font-face url()` in an inline `<style>` (#2454)

`DomParser.CascadeApplyStyleFonts` passed `stylesheet.BaseUri` to `AddFontFamilyFromUrl`. Only fetched sheets
have one (`CssParser` sets it), so an inline `<style>` (or declarative sheet) left a relative `src` unresolved
and the face was silently dropped, falling back to a system font.

Fix: the base is `stylesheet.BaseUri ?? documentBaseUri ?? renderContext.BaseUri`. The whole-document path
passes `HtmlContainerInt.DocumentBaseUri` (honors `<base href>`), read with `ParsingRoot` set because `Root` is
not assigned yet during parse. A fetched sheet's own location still wins. The prefetch address list uses the
same base so prefetch and registration agree.

Not done: a diagnostic when a face fails to load (the issue's optional ask).
