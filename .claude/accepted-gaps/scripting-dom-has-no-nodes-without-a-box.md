# The script DOM is the box tree, so the DOM standard's other node kinds are missing

`PeachBrackets.Scripting` shows a script the `CssBox` tree PeachBrackets lays out (`ScriptNode` is a thin wrapper, see
[the seam](../invariants/scripting-peachbrackets-must-not-reference-the-scripting-package.md)). The DOM standard has more node kinds than that tree
has boxes, so these have no node and the web-platform-tests of `dom/` that need them fail by design:

- **comments, processing instructions, doctypes, CDATA** as nodes (`createComment` makes an empty text node; `createProcessingInstruction` and
  `document.doctype` do not exist), and `Attr` nodes with the namespaced attribute methods (`setAttributeNS`, `getAttributeNode`...);
- **a second document**: `document.implementation.createHTMLDocument`/`createDocument`, `new Document()`, `DOMParser`, `iframe.contentDocument`
  (each throws `NotSupportedError` or is missing). Every document is one `HtmlContainerInt`, so a script cannot make another;
- **`Range`, `Selection`**: both need comments/doctypes/attrs to be meaningful, and boundary points that survive mutation (the whole `dom/ranges`
  module sits at its floor of 1 subtest);
- **`MutationObserver` records**: the tree mutates through `ScriptNode` and `CssBox` in many places and nothing reports them. A correct
  implementation hooks every mutation path (including `innerHTML` and `textContent` setters) and queues records on a microtask;
- **shadow DOM, custom elements, `Observable`, `moveBefore`**;
- **live collections**: `childNodes`, `children`, `getElementsBy*` are `NodeList`/`HTMLCollection` snapshots (a `NodeListPrototype` over an array).

**Why not now:** a second tree model (nodes that are not boxes) is the real cost, not any one interface, and it touches the serializer that turns
the scripted tree back into HTML. The measured cost of leaving it: of 7,072 subtests the `dom/` run reports, about 3,100 pass (floors in
`wpt/wpt-floors-dom.json`); the failure list from `PEACHBRACKETS_WPT_SUBTESTS` is dominated by the items above. Closing one raises that floor;
delete this file, and the matching limits in `docs/peachbrackets-scripting.md`, when the node model is no longer the box tree.

No GitHub issue is filed for this yet (it is a feature gap against the DOM standard rather than a one-rule deviation): file one per interface
(MutationObserver, Range, a second document) before building it.
