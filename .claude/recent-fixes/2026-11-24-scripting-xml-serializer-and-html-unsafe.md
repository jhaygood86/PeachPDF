# PeachBrackets.Scripting: XMLSerializer, HTML*Unsafe parsing methods, XML document fixes

Phase D of the WPT gap plan. domparsing 184 -> 452 subtests; dom-nodes +144 and html-dom +800 as side effects.

- `XMLSerializer` is script over the DOM (`Internal/XmlSerializerScripts.cs`, spliced into the bundle at `XmlSerializerMarker`): the
  DOM Parsing prefix map, prefix generation (`ns1`...), declaration recording and the `require well-formed` checks.
- **Trap: parsed XML attributes lost their namespaces.** `ParseXml` used `SetAttribute(reader.Name, ...)`, so `xmlns`/`xmlns:p` were
  plain attributes and printed twice. They now go through `SetAttributeValue` with the reader's namespace/prefix/local name.
- **Trap: `createElement` in a non-XHTML XML document makes a no-namespace element**, not an HTML one (`new Document().createElement`).
- **`DOMParser` and `new Document()` give `Document`, not `XMLDocument`** (tracked in `_plainDocuments`), and `document.location` is
  `null` for any document that is not a window's. A test in NodeModelTests had asserted the old behavior.
- `outerHTML` has a setter; `innerHTML = undefined` is the string "undefined" (only `null` is empty); `insertAdjacentHTML` and
  `outerHTML` throw `NoModificationAllowedError` for a missing or Document parent.
- `setHTMLUnsafe`, `{append,prepend,before,after,replaceWith}HTMLUnsafe`, `Document.parseHTMLUnsafe` and the `stream*HTMLUnsafe` family
  exist (streams re-parse the accumulated text into the same place on each write; no incremental parser).
- Chosen against the XLink and `<root xmlns="">` WPT cases: an attribute's own prefix is never reused when a prefix must be generated
  (DOM-Parsing issue 29), and an empty default-namespace declaration is dropped when the namespace is already null.
- Not done: the safe `setHTML`/`*HTML` variants and `sanitizer` options (no Sanitizer API), `attachShadow`/ShadowRoot targets, script
  execution from `*Unsafe` markup, the cross-frame `DOMParser` URL tests (frame windows have no `DOMParser`/`XMLSerializer`).
