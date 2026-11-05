# A property written onto a box before that box's own cascade runs is silently reset

`DomParser.CascadeApplyStyles` has a fast path in its defaulting step: while a box's `ComputedStyle` is still the shared `ComputedStyle.Default` singleton, the step skips re-asserting every property's initial value (a guaranteed no-op). Writing any property onto a box **before its own cascade has run** makes its style diverge from the singleton, so the box's own, still-pending cascade takes the other branch and resets every property, including the one just written. Symptom (issue #636): the write reads back correctly right after it, then the value is the CSS initial value after layout; a debug trace shows the write landed on the right object, which makes it look like a traversal bug.

So code that translates something *onto another box* (a table's `cellpadding`/`border` onto its cells, today) must not run from the other box's cascade. Two places are safe:

- **The target's own cascade, at the presentational-hint step** (after `TranslateAttributes`, before the author rules): the defaulting step has already run, and an author declaration on the target still beats the hint, as in a browser. `TranslateTableCellAttributes` does this, reading the nearest ancestor `table`.
- **A pass after the whole tree's cascade**: survives the reset, but is too strong for a hint: it overwrote the cell's own inline and stylesheet `padding`/`border`, `!important` included (issue #2184). Use it only for something that is meant to override author CSS.
