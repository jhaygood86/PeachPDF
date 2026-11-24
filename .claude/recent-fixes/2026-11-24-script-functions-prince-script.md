# prince-script(): text a script computes, in CSS content

Phase 5 of the Prince-parity plan. `Prince.addScriptFunc(name, fn)` registers a function; the content item `prince-script(name, item, ...)` calls it
while the document is laid out and inserts the result as text.

**Load-bearing ideas:**
- **Grammar**: `FunctionNames.ScriptFunction` plus `ScriptFunctionConverter`, added to `Converters.ContentListItemConverter`. That one converter is shared
  by `content`, `bookmark-label` and `-prince-pdf-page-label`, so the item parses in all three. The shape is `name , item , item` with each item a single
  token; `prince-script()`, a trailing comma and a missing comma are invalid declarations (dropped like any invalid value).
- **Resolution**: `CssContentEngine.ResolveContentTokens` (box `content`, pseudo-elements, `bookmark-label`) resolves each item through itself, so
  `counter()`, `attr()` and the rest work as arguments for free. `MarginBoxRenderer.ResolveContent` has its own token switch (the page counters mean the
  page being made, not a box's), so it was split into `ResolveContent` and `ResolveTokens` and the same item resolves its arguments through the latter.
- **The call-back seam**: `IScriptHost.EvaluateFunction(document, name, arguments)` (default null) reached through `HtmlContainerInt.CallScriptFunction`,
  which needs the kept-live `ScriptDocument`; the Jint host keeps the document live whenever a script function is registered
  (`WantsLayoutCallbacks`). The call is synchronous, so it works while the DOM is built (`DomParser` runs `ApplyContent` after the scripts, with the
  live document already set) and while margin boxes are made at paint time.
- A function that throws is logged by the normal script-error path and gives empty text; a missing function or host gives empty text.

**Trap:** the probe copy a script's layout reads use has `IdleScriptHost` and no live document, so a `prince-script()` there is empty. Layout reads
of an element whose size depends on such text read the empty text's size. (The real layout is unaffected.)

**Not done:** `string-set` has its own converter (`StringSetValueConverter`), so `string-set: x prince-script(...)` is not valid; and a PDF page label
parses the item but is resolved by its own code, which was not changed. The function is not cached: it is called once for each time the text is made,
so a function that returns something different each time can make two layout passes disagree.
