# XHTML: fragments, `<?xml-stylesheet?>`, MHTML bodies, view hosts and name case

The XML parser (`XhtmlParser`) covers a whole document handed to `HtmlDocument.LoadAsync` or `PdfGenerator`. Four things around it are not done:

- **Fragments are always HTML.** `DomParser.GenerateFragmentCssTree` (the declarative builder's HTML content) calls the HTML parser whatever `Syntax` says. An XML fragment needs `ConformanceLevel.Fragment` and a default-namespace parser context.
- **`<?xml-stylesheet?>` is ignored** (the reader skips processing instructions). A browser applies the sheet; `<link rel="stylesheet">` and `<style>` work.
- **MHTML:** `MimeKitNetworkLoader.GetPrimaryContents` returns `HtmlBody`, which MimeKit takes from a `text/html` part only, and the loader reports no `PrimaryContentType`, so an `application/xhtml+xml` part is neither found nor parsed as XML.
- **The `HtmlView` controls** (WinUI, Android, Avalonia, Apple) construct `HtmlDocumentOptions` themselves and leave `Syntax` at `Html`; a host showing an XHTML document sets it. The Blazor demo still maps `.xhtml` to `text/html` (`ZipMimeTypes.cs`).
- **Encoding:** the loaders decode the file or response as UTF-8 (with a BOM check) before the XML parser sees it, so the `encoding` of an XML declaration and the `charset` of a `Content-Type` are not applied by `FileUriNetworkLoader` or `HttpClientNetworkLoader` (the CLI's URL input honors `charset`).
- **Name case:** an XHTML-namespace element name is lowercased (`<DIV>` is a `div`), because the engine compares HTML names ordinally in places; a browser makes `<DIV>` an unknown element.

Part of [XHTML](../../docs/html-css-support.md#xhtml).
