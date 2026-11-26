# The WPT runner serves the checkout through an in-process server

Phase 6.0 of the Prince-parity plan. `Server/WptServer.cs` answers requests for the checkout the way `wptserve` does, with no Python and no network:
a request is a method call and the answer is bytes. Both evaluators (the reftest renderer and the testharness one) now load the test page and every
subresource through it.

**What it does:** serves files with their extension's type; `.sub.` substitution (`{{host}}`, `domains`, `hosts`, `ports`, `location`, `GET`,
`headers`, `uuid()`, `file_hash`, `header_or_default`, `url_base`; HTML-escaped in markup, not in script or CSS; an unknown placeholder is left as written);
`?pipe=sub|status()|header()`; `<file>.headers` and `__dir__.headers`; the page for `x.any.js` / `x.window.js` (`x.any.html`, 404 when the file asks for no
window), moved out of `TestharnessEvaluator`; ported `.py` handlers (`Server/WptHandlers.cs`: `common/redirect.py`, `redirect-opt-in.py`, `slow-redirect.py`,
`echo.py`, `refresh.py`, `slow.py`, `CustomCorsResponse.py`, `contenttype_setter.py`, `encoding.py`); a request log, a stash and a cookie jar. `LoadAsync`
follows redirects and keeps cookies.

**Load-bearing ideas:**
- The page origin is now `http://web-platform.test:8000`, the real WPT one (was an invented `wpt.test`), and the server answers for the sibling domains
  (`www`, `www1`, `www2`, the two punycode ones, and `not-web-platform.test`) on WPT's ports, so a test with a hard-coded address finds its server.
- An unported handler is a visible gap: a request is answered 501, and the testharness evaluator **skips** a test whose source names an existing `.py`
  with no handler, with the file as the reason.
- The server decides how a page is parsed: a `Content-Type: application/xhtml+xml` from a `.headers` file makes a `.html` test an XML document, as it is for a
  browser (`WptRenderer.IsXmlContentType`).

**Trap found by running it:** the response text must drop a UTF-8 BOM. `File.ReadAllText` did, `Encoding.UTF8.GetString(bytes)` does not, and a U+FEFF
in front of `<!DOCTYPE html>` puts the page in quirks mode: `css-grid/grid-within-flexbox-indefinite.html` went from pass to fail until
`ServerResponse.Text` stripped it.

**Effect on the floors:** css-values gained 4 reftests whose `.sub.` URLs now resolve and lost 1 (`url-image-integrity-negative.sub.html`, which was passing
without substitution only because its image never loaded; the engine ignores `integrity`): unscripted 88 to 91, scripted 98 to 102, both floors raised.
No other module changed. (A full-suite run showed css-transforms one test under its scripted floor once; alone it is 554 every time, so that is load, as
with the earlier css-text/css-multicol flake.)

**Not done:** XHR/fetch and the `.py` handlers of `xhr/`, `fetch/` and `resources/` (those directories are not fetched yet: phase 6b); websockets, h2, and
`Last-Modified`/conditional requests; `slow.py` and `slow-redirect.py` do not wait.
