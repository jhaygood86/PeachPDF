# XHTML files are parsed as XML

**Before:** every document was parsed by the HTML parser, including a `.xhtml` or `.xht` file and an HTTP response of `application/xhtml+xml`. Markup that was not well-formed was repaired, `<style>` content stayed raw text (so `&gt;` in a selector was never `>`), and a document with no DOCTYPE was in quirks mode.

**Now:** a document the loader supplies is parsed with an XML parser when the loader reports `application/xhtml+xml`: `FileUriNetworkLoader` for a `.xhtml` or `.xht` file, `HttpClientNetworkLoader` for a response of that type, and the command-line tool for the same files and URLs. The document must be well-formed (otherwise `HtmlRenderException`, with the line and position), no `html`/`head`/`body`/`tbody` is made up, `<![CDATA[...]]>` works, `&gt;` in a `<style>` is `>`, and the document is never in quirks mode. The named character entities of XHTML are known.

**To keep the old parse:** `PdfGenerateConfig.DocumentSyntax = PdfDocumentSyntax.Html` (`--document-syntax=html` on the command line). A string passed to `GeneratePdf` is unchanged (HTML) unless the config says `PdfDocumentSyntax.Xml`.

See [XHTML](../../docs/html-css-support.md#xhtml).
