# The script DOM still lacks shadow DOM, custom elements and a few small interfaces

`PeachBrackets.Scripting` now has comment, processing-instruction, CDATA and document-type nodes, `Attr` nodes with namespaces, other documents
(`createHTMLDocument`, `createDocument`, `DOMParser`, iframe `contentDocument`), live collections, `MutationObserver`, `Range` and `Selection`
(see [docs/peachbrackets-scripting.md](../../docs/peachbrackets-scripting.md)). What the `dom/` web-platform-tests still fail on, by cause:

- **shadow DOM** (`attachShadow`, `ShadowRoot`, slots) and **custom elements**: the box tree has no second tree per element. Every `*-in-ShadowRoot`
  range test and the `dom/events` composed-path tests fail for this reason.
- **`Observable`** (`dom/observable`, about 220 subtests), **`StaticRange`**, **`moveBefore`**, `createValueRange`/`OpaqueRange`, `Element.animate`.
- **`ProcessingInstruction` pseudo-attributes** (`dom/nodes/processing-instruction-attributes*`) and the `DOMTokenList` iteration helpers.
- **Event dispatch details**: `passive` listeners and the default-passive touch/wheel events (`dom/events/passive-by-default.html`).
- **Namespaces live on the script side** (the wrapper of each element and attribute), not in the box tree, so they do not survive a re-parse of the
  scripted page; a script that builds XML in the page's own document sees it come back as HTML.
- The per-script memory limit (256 MiB) stops the largest `Range-set.html` style loops one test short.

**Why not now:** each is a feature of its own, none is needed to print a page, and the box tree has no place for a shadow tree. Raise the floors in
`wpt/wpt-floors-dom.json` as they close. No GitHub issue is filed yet: file one per interface (shadow DOM, custom elements, `Observable`) before
building it.
