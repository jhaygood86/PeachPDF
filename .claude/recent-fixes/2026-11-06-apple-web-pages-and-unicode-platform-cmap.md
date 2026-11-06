# The two web pages in the Apple demos drew nothing

**Symptom:** opening peachpdf.net or peachstatetechnologies.com in the macOS demo showed an empty stage and the window looked hung (a Debug build of the example actually died on a `Debug.Assert`, so no error reached the user).

**Three separate causes, found by running the pages one by one:**

1. **No base address.** A style sheet is loaded while the document is still being parsed, so only `RenderContext.BaseUri` applies to it - the page's own `<base href>` is not read until the parse is over (`HtmlContainerInt.DocumentBaseUri`). WinUI has `DocumentBaseUri` for that; the Apple view had no equivalent, so `/assets/css/style.css` reached the loader as a relative address and was dropped. `HtmlView.DocumentBaseUri` now feeds `HtmlViewRenderContext.BaseUri`; both demos set it for a web showcase. The rewriter's inserted `<base>` is still useful for images and links.
2. **macOS system fonts with a Unicode-platform cmap.** `CMapTable.Read` only accepted platform 0 / encoding 3 as a BMP subtable. `/System/Library/Fonts/HelveticaNeue.ttc` carries one format-4 subtable on platform 0 / encoding 1 (and 3), so "Helvetica Neue" - named by the peachpdf.net style sheet - threw "Font has no usable platform or encoding ID". Encodings 0-3 are all a BMP repertoire; the format check beside it already keeps a non-format-4 subtable out. `UnicodePlatformCmapFontTests` fails for encodings 0-2 without the change.
3. **A failed font load poisoned the process-wide cache.** `FontFactory.ResolveTypeface` registered the resolver info under its keys *before* creating the font source, so when the source threw, every later request for that face was answered from the cache with an info whose source did not exist (`GetFontSourceByFontName` asserting, or returning null in Release). The info is now registered after the source exists.

**Not done:** no test for (3) - it needs a system font that fails to parse, and the global cache is shared by the whole test process (see CLAUDE.md on `FontFactory` statics).
