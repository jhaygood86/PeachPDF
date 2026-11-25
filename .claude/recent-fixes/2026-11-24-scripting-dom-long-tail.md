# PeachBrackets.Scripting: the DOM long tail

Phase E of the WPT gap plan. dom-nodes 8041 -> 12382 (of 13309; most of it from the characterSet tests loading at all), dom-events 273 -> 376,
dom-other 275 -> 468 (Observable), domparsing 477 -> 498.

- **Names follow the relaxed DOM rules** (`XmlNames.IsElementLocalName`/`IsNamespacePrefix`/`IsDoctypeName`): an element local name starts with
  a letter, `:`, `_` or a non-ASCII character and has no white space, NUL, `/` or `>`; `createElementNS` splits at the *first* colon and
  checks each side (`0:a` is fine, `a:0` is not); a doctype name only excludes white space, NUL and `>`. `createAttribute` keeps the old
  `Name` check. `createProcessingInstruction` too.
- **Each document has its own `DOMImplementation`** (`ImplementationOf`); before, the main document's was shared, so
  `other.implementation.createDocumentType()` made a node of the wrong document. Documents made by `createDocument`,
  `createHTMLDocument` and `new Document()` report `about:blank` as their URL and (XML ones) `CSS1Compat`.
- **Events**: the interfaces are a table (`EventInterfaces`: parent and `member:default` pairs) so `MouseEvent` has `clientX`...,
  prototypes chain `MouseEvent` -> `UIEvent` -> `Event`, init values are converted to the member's type, and `createEvent` knows the
  remaining legacy names. Listeners carry `passive` (default true for `touchstart`/`touchmove`/`wheel`/`mousewheel` on the window,
  document, its element and body); `preventDefault` does nothing while one runs; the `signal` option removes the listener on abort.
- **`document.characterSet`/`charset`/`inputEncoding`** come from the first `meta charset` (or Content-Type `http-equiv`) through the Encoding
  Standard label table in `EncodingLabels.cs` (extracted from the WPT test data, which lists every label); UTF-16 labels read as UTF-8 there.
- **Frames now load and fire `load`**: an iframe put in the page loads its document and gets a `load` event after the inserting script
  (`HandleFrameMutation`), and the frames a page starts with load before the window's `load` (`LoadInitialFrames`); each frame fires once
  (`_framesLoaded`). Before, a frame loaded only when `contentDocument` was first read, so tests waiting on `onload` timed out.
- Processing instructions have the pseudo-attribute API (`getAttribute`, `setAttribute`, `removeAttribute`, `toggleAttribute`...) over their data.
- `Observable`, `Subscriber`, `EventTarget.when` and `reportError` are script (`ObservableScripts.cs`), run at startup; `DOMTokenList` iterates.
- Not done: HTML parser PIs (the parser makes comments, as HTML does), `moveBefore`, shadow DOM, `Document.createAttribute` XML names,
  `ErrorEvent` line numbers for reported errors, the body/frameset window event handlers, `click()` dispatch details.
