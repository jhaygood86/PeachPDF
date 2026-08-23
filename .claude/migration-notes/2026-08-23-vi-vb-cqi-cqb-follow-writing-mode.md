# `vi`/`vb`/`cqi`/`cqb` now resolve against the root element's/query container's own `writing-mode`

Previously, `vi`/`vb` always resolved identically to `vw`/`vh` (the page box's physical width/height), and
`cqi`/`cqb` always resolved identically to `cqw`/`cqh` (the nearest ancestor query container's physical
width/height) — regardless of the document root's or container's own `writing-mode`.

Now, under a `vertical-rl`/`vertical-lr` root element, `vi` resolves to 1% of the page box's **height**
(not width) and `vb` resolves to 1% of the page box's **width** (not height) — the inline/block axis
rotates with the root's own writing mode, per CSS Values and Units 4 §6.2. The same rotation applies to
`cqi`/`cqb` against a `vertical-rl`/`vertical-lr` query container, per CSS Containment 3 §6.2.

A document using `vi`/`vb`/`cqi`/`cqb` under a horizontal (`horizontal-tb`, the default) root/container is
unaffected — these units continue to resolve identically to `vw`/`vh`/`cqw`/`cqh` there, since inline/block
and width/height coincide under `horizontal-tb`. `vw`/`vh`/`cqw`/`cqh` themselves (and `vmin`/`vmax`/
`cqmin`/`cqmax`, which are defined against `vw`/`vh` and `cqi`/`cqb` respectively per spec) are unaffected
in every case.

`cqh`/`cqb`/`cqi` against a `container-type: size` query container with a definite, explicit-length
`height` (e.g. `height: 200px`) now resolve to that container's real height instead of `0` — a genuinely
separate, pre-existing bug found while adding coverage for the above (issue #805), independent of
writing-mode (it also affected a plain `horizontal-tb` container's `cqh`). A **percentage** container
`height` (e.g. `height: 50%`) whose own base isn't itself resolved yet can still resolve to `0` — tracked
separately as issue #807.

`@container (inline-size ...)`/`(block-size ...)` condition matching now also follows the query
container's own `writing-mode` the same way `cqi`/`cqb` do — previously `inline-size`/`block-size`
conditions were evaluated identically to `width`/`height` (always physical), so e.g. `@container
(inline-size > 300px)` against a `vertical-rl` container tested its physical width instead of its true
inline axis (physical height). `width`/`height`/`aspect-ratio`/`orientation` conditions are unaffected —
they stay physical per spec (issue #806).

Issues [#795](https://github.com/jhaygood86/PeachPDF/issues/795),
[#805](https://github.com/jhaygood86/PeachPDF/issues/805),
[#806](https://github.com/jhaygood86/PeachPDF/issues/806).
