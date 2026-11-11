# MimeKit 5.0 upgrade (MimeKit.Core)

PeachPDF and PeachBrackets now reference `MimeKit.Core` 5.0.0 instead of `MimeKit` 4.18.2. Only MIME parsing
(`MimeKitNetworkLoader`) and `MimeKit.Text.HtmlTokenizer` are used, so the cryptography half of MimeKit (and its
`System.Security.Cryptography.Pkcs` pin) is gone. Namespaces are unchanged.

- `HtmlTokenizer.ScriptingEnabled` defaults to false in 5.0 and is set explicitly in `HtmlParser.ParseDocument`.
  With it off, `<noscript>` children arrive as ordinary tokens, so the 4.x workaround that re-parsed a noscript's
  single text run as markup (`noScriptContent` parameter) was removed. `NoScriptParsingTests` covers it.
- The tokenizer now follows html5lib/the HTML spec (CDATA only in SVG/MathML, legacy entity rules in attributes);
  the full engine, library, CLI and consumer suites pass unchanged on net10.0.
- Not run locally: Release configuration and the CLI NativeAOT publish smoke.
