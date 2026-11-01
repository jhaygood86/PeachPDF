# A change of hover/focus state reloads the document

`SetPointerStateAsync`/`SetFocusAsync` apply a state by parsing and cascading the source again with the
new state, then laying it out (the same wholesale redo the container-query convergence loop uses). That
keeps every selector form correct (`+`, `~`, `:has()`, `:is()`, rules that move or resize things) but costs
a full cascade and layout per change that a rule reads, rather than restyling only the affected elements.

- A document built with the declarative API (`CreateAsync`) has no source to parse again, and a document
  whose text was edited in place would lose its edits; both report `Applied = false` with a reason.
- Transitions and animations a state change would start are not run: the document jumps to the new style.
- The regions reported for a state change that moved nothing are the changed elements and their contents,
  widened by 24 points; a shadow or outline that reaches farther than that is not covered.

Restyling only the affected subtree needs the cascade to be re-runnable on a laid-out box tree, which it
is not (generated content, anonymous boxes and `display: contents` shells are built from its result).
