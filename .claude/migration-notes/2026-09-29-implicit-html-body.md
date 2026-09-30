# Implicit `<html>`/`<body>` elements

**Before:** a document that omitted its `<html>`/`<body>` start tags (e.g. `<style>body{color:red}</style><p>a</p>`) got no such elements, so `body { }` and `html { }` rules — including the UA sheet's 8px body margin — did not apply.

**Now:** the parser inserts both implicitly (WHATWG "before html" / "after head" insertion modes), so those rules match exactly as with the tags written out. Leading head-content elements (`style`, `meta`, `link`, ...) stay outside the implicit body. Bare fragments therefore pick up the default 8px body margin; add `body{margin:0}` to keep the old geometry.
