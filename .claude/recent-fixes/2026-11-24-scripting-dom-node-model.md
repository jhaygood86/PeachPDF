# The script DOM gets the node kinds, documents and observers the box tree lacked

**Idea.** The box tree stays the model; what it has no box for is a `CssBox` holding no tag and no text, tagged in a `ConditionalWeakTable` (`SpecialNode`: comment,
processing instruction, document type) or a Text box marked as CDATA. The scripted tree is always serialized and re-parsed before layout, so these boxes never reach layout;
the serializer writes them back as `<!--...-->`, `<?...>` and `<!DOCTYPE ...>`, and the document's own DOCTYPE became a real node (written from it, not from the mode).

**Other documents** are `ScriptDocument`s over their own root box sharing the page's container (`CreateDocument`/`ParseDocument`); a node moves between them by `Adopt`,
which re-homes the wrapper (`ScriptNode.OwnerDocument` is settable internally) so JS identity survives. Every document-prototype method works on `D(self)`, not the page's document.

**Mutations** are one public event (`ScriptDocument.Mutated`, `ScriptMutation`), raised only when something listens, from every `ScriptNode` mutator; `replaceChild`,
`textContent` and `innerHTML` raise one record each (spec "replace all"). `MutationObserver`, live `Range`s and live lists (version-checked against `ScriptDocument.Version`) all hang off it.

**Traps found by running it.**
- `Node.DOCUMENT_POSITION_*` were only on the prototype, not the `Node` interface: the whole `dom/ranges` module was reading `undefined & ...` and every comparison "failed". Copy every constant to the constructor.
- `ObjectInstance` subclasses must assign `Prototype` themselves (a `SelectionObject` without it has no methods).
- A test that calls `takeRecords()` at the end of a script empties the queue the microtask would have delivered.
- `console.log(a, b)` joins with a space, not a comma; test strings built with commas must `join()`.
- Under load the scripted floors for css-grid/css-backgrounds read 1-4 lower in a full-suite run and are at floor alone; the dom floors for nodes/ranges vary by ~100 for the same reason, so they are committed a little below a measured run.

**Not done.** See [the gap file](../accepted-gaps/scripting-dom-parts-still-missing.md). **Evidence.** dom floors (passing subtests): nodes 2582 -> 7800, ranges 1 -> 37000, traversal 42 -> 1439, events 234 -> 245, other 214 -> 269; 93 Scripting tests, 1446 PeachBrackets tests.
