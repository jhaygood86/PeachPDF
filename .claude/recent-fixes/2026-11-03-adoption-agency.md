# The adoption agency algorithm and the in-body end tag rules

First of a series that brings `HtmlParser` to the HTML Standard's tree construction (this one: the stack, the list of active formatting elements,
scopes, the adoption agency algorithm, reconstruction and the in-body start and end tag rules; table modes with foster parenting, then the document
modes and `<template>`, follow).

- **The stack of open elements is the chain of parents.** `HtmlParser.TreeConstruction.cs` keeps no second stack: `ParserState.Current` is the current
  node and every "pop until X" is `Current = X.ParentBox`. The one step that rearranges the stack, the adoption agency, rearranges the tree to match
  (the furthest block goes under the common ancestor, the copies nest around it), so the chain stays the stack. Do not add a parallel list without
  keeping it in step with the tree - the cheap derivation is the reason there is nothing to desynchronise.
- **The list of active formatting elements holds boxes.** Entries are replaced in place when reconstruction or the algorithm copies an element, a
  transient bookmark entry stands for the algorithm's "position in the list" (an index would shift under the removals), and markers carry the element
  they stand for. A marker is dropped, with the entries after it, when its element is no longer on the chain (`ClearClosedMarkers`, once per token),
  which is "clear the list up to the last marker" without hooking every place an element can close.
- **Copies never alias the original's `HtmlTag`.** `CopyOf` copies the attribute dictionary; `HtmlTag.SetAttribute` mutates it (form seeding, overrides).
  Copies are made with `CssBox.CreateBox(tag)` (no parent) and attached afterwards; `IsWithinForeignContent` is fixed at construction, which is right
  because the algorithm never runs for a foreign current node.
- **Source ordinals need nothing.** They are numbered by a walk after the parse (`DomParser.NumberElements`), so moved and copied elements are numbered
  in their final order and a second parse of the same source numbers them identically (the re-parse for `_formChanges` depends on that).
- **Depth.** Reopening can nest without bound (`<div><b id=1>t</div><div><b id=2>t</div>`...: every `<div>` reopens all the earlier, different `<b>`s),
  so `ReconstructFormatting` and the algorithm check `MaxElementDepth`; that input is an `HtmlRenderException` rather than a stack overflow. Browsers
  cap the same shape at 512.
- **What changed for documents that were already nested correctly: nothing**, which the engine suite (17,448 tests) shows. What changed is the shapes the
  old cursor could not see. A start tag now closes a `<p>` open anywhere in button scope (`<p><span><div>`, not only `<p><div>`), so four test fixtures
  that put a `<div>` in a `<p>` as shorthand for "a block in an inline" now use a `<div>` as the container. An end tag now closes only an element in
  scope and is ignored when a special element is in the way (`<span><div></span>`), `</br>` is a `<br>`, and `</h4>` closes an open `<h3>`.
- **Still the old handling** (named in `NearestAncestorEndTags`): the end tags of `html`, `body`, `head`, `frameset`, the table elements, `select`,
  `option`, `optgroup` and `template` close the nearest open element of that name, and `CanEndTagBeOmitted` still closes cells, rows, table sections and
  options; the table, select and document insertion modes replace them.
- **Not done:** foster parenting, `form` element pointer (`</form>` closes like a block, not "remove from the stack"), the quirks-mode `<table>` rule,
  `<a>` left in the middle of the stack when the algorithm gives up (a stack removal the chain cannot express; unreachable without table modes),
  tokenizer states (MimeKit).
