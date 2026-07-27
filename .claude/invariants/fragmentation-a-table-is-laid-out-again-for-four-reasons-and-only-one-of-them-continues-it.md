# A table is laid out again for four reasons and only one of them continues it

_CSS Fragmentation Level 3. Tracker: [#390](https://github.com/jhaygood86/PeachPDF/issues/390)._

`CssLayoutEngineTable` is constructed afresh every time it runs, so anything it must not decide twice
has to be gated on something. **The gate is the resumption record, never "has this table been laid
out before"**, and the difference is not a nicety: the engine runs again over a table it has already
laid out for four reasons, and three of them are *fresh layouts that must start from the markup*.

| why it runs again | what the run is |
|---|---|
| the per-page-width reflow loop | fresh — the table's width changed, so everything downstream of it has to be re-derived |
| `ShrinkToFit` | fresh |
| a §4.3 mover relocating the subtree, which lays it out again at its destination | fresh |
| a resumed fragmentainer pass | **a continuation** — earlier rows are already emitted |

A guard written as "once per box" (the shape `CssBox._tableFixed` correctly has, because inserting
rowspan placeholders really is once per box per document) makes the first three runs inherit the last
run's output as if it were input. A guard written as "only when `resume is not null`" does not, and it
is also self-disabling: no table receives a record today, so such a guard is inert until stage 4 wires
one up.

The measured symptom of getting it the wrong way round is not subtle — a repeating `<thead>`'s
detached group never returns to the table's child list, so the second layout finds only its stale
proxies, takes the first for the header and classifies the rest as body rows, and throws
`Sequence contains no elements` positioning a row with no cells ([#353](https://github.com/jhaygood86/PeachPDF/issues/353)).
What a *continuation* must not redo is listed on `TableSetup`, each entry with what re-running it
destroys.
