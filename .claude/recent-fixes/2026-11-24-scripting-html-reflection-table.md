# PeachBrackets.Scripting: the HTML reflection table

Phase H of the WPT gap plan. html-dom 34919 -> 63283 of 64541 subtests (the remaining ones are `innerText`/`outerText`, `dir=auto`
directionality, named items, `ariaXxxElement` and a few obsolete body/html colors); dom-events +22, dom-nodes +33, domparsing +25.

- `Internal/ReflectionTable.cs` is data: the attributes of 74 tags (IDL name, type, keywords, defaults, limits) plus the global and ARIA
  ones, extracted from the WPT `elements-*.js` files (which are the HTML IDL in compact form) and checked in as JSON. Regenerate it
  from those files when the pinned WPT commit moves. `ScriptRuntime.Reflection.cs` implements each type's get/set rules
  (parsing integers and floating-point numbers, limited/clamped numbers, enumerated attributes with invalid and missing defaults,
  nullable strings, token lists, URLs resolved with the bundle's URL parser).
- Per-interface prototypes now carry the typed properties (`ElementInterfaces` lists every HTML interface), so `HTMLTableCellElement`
  has `colSpan` and `div` has no `href` of its own. The old generic `TextAttributes`/`BooleanAttributes` stay on `Element.prototype` for
  names no interface claims (`value`, `href`...), so a page that reads them from an element the table does not know still works.
- **Trap: the table entries are keyed by tag, not interface.** Two tags of one interface (`td`/`th`) are merged by name; the WPT data
  for `input.type` in `elements-forms-weekmonth.js` is a variation of the full list and must not be merged over it.
- **Trap: an element made by `createElementNS` outside the HTML namespace must get plain `Element.prototype`** (the interface
  properties belong to HTML elements only); `CreateElementNs` and `createElement` in a non-XHTML XML document reset the prototype.
- Two quirks in the WPT data: a few entries nest the description under `type`, and `video.preload` gives its default as the list the user
  agent chooses from (the second, "metadata", is used).
- Not done: `innerText`/`outerText` (rendering-dependent text), `dir=auto` resolution, `<body>` legacy color properties on the document,
  `Element`-valued ARIA reflection, `tabIndex` defaults beyond focusable/not.
