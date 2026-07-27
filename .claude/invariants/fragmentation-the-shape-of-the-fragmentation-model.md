# The shape of the fragmentation model

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

What a change in this area has to keep true. Each clause is settled work with an issue behind it;
the point of the file is that the model is easy to violate one clause at a time.

**Layout is a driver over fragmentainers** ([#321](https://github.com/jhaygood86/PeachPDF/issues/321)).
It targets one, and where content does not fit reads back a **break token** — a chain with one link
per ancestor, from the fragmentation-context root down to the box that stopped — then opens the next
fragmentainer and re-enters there. A document that fits takes a single pass.

**Layout emits its own fragments as it fills**
([#331](https://github.com/jhaygood86/PeachPDF/issues/331)): `FragmentTree` →
`FragmentainerFragment` → `BoxFragment` → `LineFragment`/`TextFragment`, under
`Html/Core/Fragments/`, with `FragmentEmitter` as the seam. **Paint consumes only that** and never
reads geometry off `CssBox` ([#298](https://github.com/jhaygood86/PeachPDF/issues/298),
[#325](https://github.com/jhaygood86/PeachPDF/issues/325)), in its own phase under
`Html/Core/Paint/` ([#324](https://github.com/jhaygood86/PeachPDF/issues/324)). Each fragment carries
**geometry of its own** ([#366](https://github.com/jhaygood86/PeachPDF/issues/366)), which is what
lets two columns of one box exist at all.

**A multi-column column is a real fragmentainer** in §2's sense
([#322](https://github.com/jhaygood86/PeachPDF/issues/322), #366), so the block-axis break machinery
works inside one without being told about columns.

**A break value names the context it speaks for**
([#304](https://github.com/jhaygood86/PeachPDF/issues/304),
[#312](https://github.com/jhaygood86/PeachPDF/issues/312),
[#313](https://github.com/jhaygood86/PeachPDF/issues/313)), and each context is realized by a
different **vehicle**: a page break by *placement*, a column break by a *break decision*.
`FragmentationContext` has no region member, which is what makes `region`/`avoid-region` inert by
construction rather than by omission ([#319](https://github.com/jhaygood86/PeachPDF/issues/319)).

**"Did this cross out of its fragmentainer?" is one question with two bands**
([#400](https://github.com/jhaygood86/PeachPDF/issues/400) (b)), asked with two named boundary
conventions rather than an epsilon spelt out at every call site (#400 (a)) — and **a measurement
pass names no fragmentainer at all** (#400 (c)): a box laid out at a provisional position it is
about to be moved away from cannot ask a fragmentation question, rather than asking one that is
suppressed.

**§4.3's relaxation ladder reaches its last rung.** Every tier is allowed to refuse — the
keep-with-next run is trimmed, then dropped, then the container is left behind, then the constraint
is given up — because the backstop underneath them all cannot: a pass reproducing the record it was
handed has its remainder laid out monolithically, overflowing one fragmentainer rather than losing
content ([#404](https://github.com/jhaygood86/PeachPDF/issues/404)).

**Re-entering a pass rolls the box tree back to the state that pass began in**
([#415](https://github.com/jhaygood86/PeachPDF/issues/415)), through one shared
`PassRewind.RollBackTo`. A box's prologue is once per *layout*, so a re-entered pass gets none of
what it settles back unless the rollback lets it.

The rendering-side counterpart lives in
[docs/architecture.md](https://github.com/jhaygood86/PeachPDF/blob/main/docs/architecture.md) §5–§6,
which is the reader-facing description of the same pipeline.
