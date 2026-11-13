# A `<link>`/`@import` stylesheet resolves against `<base href>`, and a relative reference with no base is skipped

Found by the WPT runner: on a host with no base address of its own (`RasterRenderContext`, whose `BaseUri` is null) every test that linked a
relative stylesheet died with `HtmlRenderException: Exception in handling stylesheet source` (`InvalidOperationException: This operation is not
supported for a relative URI`) - over 100 tests in css-grid and css-flexbox alone.

## Cause
`HtmlContainerInt.DocumentBaseUri` answered `RenderContext.BaseUri` while `Root` was null, and `Root` is only assigned after
`DomParser.GenerateCssTree` returns - which is after the `<link>`/`<style>` stylesheets are collected. So those two never saw `<base>`, and
with no host base the reference stayed a relative `Uri`, which `StylesheetLoadHandler` then asked for `.AbsoluteUri`.

## Fix
- `HtmlContainerInt.ParsingRoot`: `GenerateCssTree` hands the tree it is building to the container for the length of the stylesheet
  collection (`try/finally`, so no stale tree survives an exception). `DocumentBaseUri` reads `Root ?? ParsingRoot`; the memo key is that
  tree, and `Root` is later assigned the same object, so the answer carries over.
- The base now follows the HTML "document base URL": the first `<base>` that **has an `href`** (a `<base target>` before it no longer hides
  it), resolved against the host's base address, kept only if absolute. A `<base href>` that does not make an absolute address is ignored.
  It used to throw (`new Uri(href)`) on a malformed or relative href - a render failure for a document that merely had a bad `<base>`.
- `StylesheetLoadHandler`: a reference that is not absolute after resolution returns "no stylesheet" (as `ImageLoadHandler` already did),
  as its own comment always claimed. `CommonUtils.ResolveAgainstDocumentBase` is unchanged (image and cursor callers rely on it).
- The WPT runner's workaround (rewriting stylesheet hrefs and `@import` to absolute URLs) is gone; the same css2-flow sample passes the same
  tests without it.

## Not done
`@font-face url(rel)` inside an inline `<style>` resolves with a null stylesheet base, so it ignores `<base>` (it fails gracefully).
`CssBoxImage.cs` line ~162 reads `.AbsoluteUri` on a possibly-relative `Uri`; not checked.

## Evidence
`DocumentBaseUriTests` (engine, `HtmlRenderContext`) and `PeachBrackets.Tests/StylesheetBaseTests` (public API, `RasterRenderContext`, requests
recorded), Engine.Tests 19,816 and PeachPDF.Tests 1,978 tests green on net10.0.
