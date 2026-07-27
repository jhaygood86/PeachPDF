# Recent fixes

One file per fix, named `YYYY-MM-DD-<slug>.md`, where the date is when the fix landed on `main`.
These are engineering history — the *why* behind a change, the traps found on the way in, and the
evidence each conclusion rests on — not user-facing documentation. See
[CLAUDE.md](../../CLAUDE.md#recent-fixes) for how to add one and when to delete one.

**A fix is not recent once it is more than 30 days old.** Delete it — and before you do, make sure
anything in it a reader still needs has reached `docs/**`/`README.md` (user-facing behaviour) or the
accepted-gaps list in CLAUDE.md (a deviation we have decided to live with). If it has not, that
migration is part of the deletion, not a follow-up.

## Index (newest first)


### 2026-07-27

- [§4.3's last resort runs, and the method that stopped it now returns the exception instead of throwing it](2026-07-27-4-3-s-last-resort-runs-and-the-method-that-stopped-it-now-return.md)
- [A grid track grows toward its limit with the space that is there](2026-07-27-a-grid-track-grows-toward-its-limit-with-the-space-that-is-there.md)
- [A column's recorded band is the one it filled, not the target it was given](2026-07-27-a-columns-recorded-band-is-the-one-it-filled-not-the-target-it-w.md)
- [A selector PeachPDF cannot match is recognized rather than unknown](2026-07-27-a-selector-peachpdf-cannot-match-is-recognized-rather-than-unkno.md)
- [A table that did not break is moved by the same mover as everything else that cannot be broken](2026-07-27-a-table-that-did-not-break-is-moved-by-the-same-mover-as-everyth.md)
- [A break value between two table rows is read](2026-07-27-a-break-value-between-two-table-rows-is-read.md)
- [A subtree holding a repeating-header table is relocated like any other](2026-07-27-a-subtree-holding-a-repeating-header-table-is-relocated-like-any.md)
- [A table with a repeating header survives being laid out again](2026-07-27-a-table-with-a-repeating-header-survives-being-laid-out-again.md)
- [A measurement pass names no fragmentainer at all](2026-07-27-a-measurement-pass-names-no-fragmentainer-at-all.md)
- ["Did this cross out of its fragmentainer?" is one question with two bands](2026-07-27-did-this-cross-out-of-its-fragmentainer-is-one-question-with-two.md)
- [A run travels at a column boundary, and a page break does not stop at one](2026-07-27-a-run-travels-at-a-column-boundary-and-a-page-break-does-not-sto.md)
- [The two page-boundary conventions have a name each](2026-07-27-the-two-page-boundary-conventions-have-a-name-each.md)
- [The collapsed margin before a child is the frame's question](2026-07-27-the-collapsed-margin-before-a-child-is-the-frame-s-question.md)

### 2026-07-26

- [A block-level box's position is assigned by the frame above it](2026-07-26-a-block-level-box-s-position-is-assigned-by-the-frame-above-it.md)
- [PeachPDF runs in a browser, and the two things that stopped it are library defects rather than demo plumbing](2026-07-26-peachpdf-runs-in-a-browser-and-the-two-things-that-stopped-it-ar.md)
- [§5.4's line minimums are decided once the per-page measures have settled](2026-07-26-5-4-s-line-minimums-are-decided-once-the-per-page-measures-have.md)
- [An elliptical radial gradient's pattern matrix composes with the CTM instead of replacing it](2026-07-26-an-elliptical-radial-gradient-s-pattern-matrix-composes-with-the.md)
- [A `clone` fragment at a column break claims the room layout reserved for it](2026-07-26-a-clone-fragment-at-a-column-break-claims-the-room-layout-reserv.md)
- [A whole-box `slice` decoration is measured against the concatenation of a box's column fragments](2026-07-26-a-whole-box-slice-decoration-is-measured-against-the-concatenati.md)
- [The driver goes back to the pass that placed the run head](2026-07-26-the-driver-goes-back-to-the-pass-that-placed-the-run-head.md)
- [A break value names the fragmentation context it speaks for](2026-07-26-a-break-value-names-the-fragmentation-context-it-speaks-for.md)
- [`widows` moves the lines it takes, not the whole box](2026-07-26-widows-moves-the-lines-it-takes-not-the-whole-box.md)
- [The container travels with the §4.3 movers too](2026-07-26-the-container-travels-with-the-4-3-movers-too.md)
- [A box split at a column boundary leaves that edge open](2026-07-26-a-box-split-at-a-column-boundary-leaves-that-edge-open.md)
- [An abandoned multicol fill attempt is undone rather than skipped](2026-07-26-an-abandoned-multicol-fill-attempt-is-undone-rather-than-skipped.md)
- [§4.3's relaxation ladder stated, and `orphans` decided at the break point](2026-07-26-4-3-s-relaxation-ladder-stated-and-orphans-decided-at-the-break.md)
- [A fragment carries its own geometry, so a child splits across a column](2026-07-26-a-fragment-carries-its-own-geometry-so-a-child-splits-across-a-c.md)
- [Layout emits its fragments, and a forced break travels with the container the box begins](2026-07-26-layout-emits-its-fragments-and-a-forced-break-travels-with-the-c.md)
- [Flex lines and grid rows are break points](2026-07-26-flex-lines-and-grid-rows-are-break-points.md)
- [A multi-column column is a real fragmentainer](2026-07-26-a-multi-column-column-is-a-real-fragmentainer.md)
- [A break before a container's first child is a real break point](2026-07-26-a-break-before-a-container-s-first-child-is-a-real-break-point.md)

### 2026-07-25

- [Retroactive break movers stated as break decisions](2026-07-25-retroactive-break-movers-stated-as-break-decisions.md)
- [Monolithic content named and honoured, and break values carried onto structural clones](2026-07-25-monolithic-content-named-and-honoured-and-break-values-carried-o.md)
- [Directional forced breaks, and `avoid-page` vs `avoid-column`](2026-07-25-directional-forced-breaks-and-avoid-page-vs-avoid-column.md)
- [A resumed pass no longer re-opens an inline box it never closed, and the overflow clip comes from the fragment](2026-07-25-a-resumed-pass-no-longer-re-opens-an-inline-box-it-never-closed.md)
- [`box-decoration-break` honored at page and line breaks, and room reserved for `clone`](2026-07-25-box-decoration-break-honored-at-page-and-line-breaks-and-room-re.md)
- [Layout fragments during layout, not after it](2026-07-25-layout-fragments-during-layout-not-after-it.md)
- [`box-decoration-break` stored and exposed on `CssBox`](2026-07-25-box-decoration-break-stored-and-exposed-on-cssbox.md)
- [Complete `break-*` value grammar](2026-07-25-complete-break-value-grammar.md)
- [`FragmentPainter`: paint extracted out of `CssBox` into its own phase](2026-07-25-fragmentpainter-paint-extracted-out-of-cssbox-into-its-own-phase.md)

### 2026-07-24

- [Fragment-aware box model: layout now produces an immutable fragment tree, and paint consumes it](2026-07-24-fragment-aware-box-model-layout-now-produces-an-immutable-fragme.md)
- [Full SVG `<text>` layout model](2026-07-24-full-svg-text-layout-model.md)
- [CSS `font-palette` + `@font-palette-values` + `palette-mix()`](2026-07-24-css-font-palette-font-palette-values-palette-mix.md)
- [SVG `<textPath>`, and gradient/pattern `fill` + `stroke` on `<text>`](2026-07-24-svg-textpath-and-gradient-pattern-fill-stroke-on-text.md)
- [Color emoji via vector glyph outlines (`COLR`/`CPAL`, v0 + v1)](2026-07-24-color-emoji-via-vector-glyph-outlines-colr-cpal-v0-v1.md)

### 2026-07-23

- [`@media` feature-query evaluation](2026-07-23-media-feature-query-evaluation.md)
- [`peachpdf` command-line tool + five small library additions to back its options](2026-07-23-peachpdf-command-line-tool-five-small-library-additions-to-back.md)
- [Trimming + NativeAOT compatibility hardening; all `DllImport` → source-generated `LibraryImport`](2026-07-23-trimming-nativeaot-compatibility-hardening-all-dllimport-source.md)
- [CSS Grid v1 sizing/placement gaps](2026-07-23-css-grid-v1-sizing-placement-gaps.md)
- [CSS Grid `subgrid` + a typed cascade value `CssProperty<T>`](2026-07-23-css-grid-subgrid-a-typed-cascade-value-cssproperty-t.md)
- [Nested `opacity` no longer double-blends — the general gap is fully closed for CSS and SVG](2026-07-23-nested-opacity-no-longer-double-blends-the-general-gap-is-fully.md)
- [`@page` `em`/`ex` resolve against the page context's own `font-size`](2026-07-23-page-em-ex-resolve-against-the-page-context-s-own-font-size.md)
- [CSS Grid `grid` / `grid-template` mega-shorthands](2026-07-23-css-grid-grid-grid-template-mega-shorthands.md)
- [`flex-direction: column` cross-axis (horizontal) alignment](2026-07-23-flex-direction-column-cross-axis-horizontal-alignment.md)
- [Gradient `in <color-interpolation-method>` prelude validated at parse time; two parsers unified onto one grammar](2026-07-23-gradient-in-color-interpolation-method-prelude-validated-at-pars.md)
- [CSS Grid named lines + `grid-template-areas`](2026-07-23-css-grid-named-lines-grid-template-areas.md)
- [SVG transparent gradient stroke's soft mask leaking onto later paint](2026-07-23-svg-transparent-gradient-stroke-s-soft-mask-leaking-onto-later-p.md)
- [CSS Grid layout](2026-07-23-css-grid-layout.md)
- [Recursive prefetch of `<image>` hrefs nested inside embedded `image/svg+xml` payloads](2026-07-23-recursive-prefetch-of-image-hrefs-nested-inside-embedded-image-s.md)
- [SVG `clipPathUnits="objectBoundingBox"`](2026-07-23-svg-clippathunits-objectboundingbox.md)
- [SVG `<image>` with a network/file-path `href`](2026-07-23-svg-image-with-a-network-file-path-href.md)
- [`AttributeSelectorFactory` made trim/AOT-safe](2026-07-23-attributeselectorfactory-made-trim-aot-safe.md)
- [Cascade-layer correctness: `!important` layer reversal, nested-layer tree ordering, at-rules inside `@layer`, and layer-aware `revert-layer`](2026-07-23-cascade-layer-correctness-important-layer-reversal-nested-layer.md)
- [`aspect-ratio` both directions + replaced elements + indefinite-% height + min/max clamping](2026-07-23-aspect-ratio-both-directions-replaced-elements-indefinite-height.md)

### 2026-07-22

- [CSS Nesting](2026-07-22-css-nesting.md)
- [`object-fit` / `object-position` on all replaced elements](2026-07-22-object-fit-object-position-on-all-replaced-elements.md)
- [Utility-CSS (Tailwind) compatibility tranche: `:where()`, `@layer`, `@supports`/`@container` indexing, and CSS Color 4/5 functions — plus a color-parsing consolidation that fixed a latent `hsl()`/`hwb()` crash](2026-07-22-utility-css-tailwind-compatibility-tranche-where-layer-supports.md)
- [SVG `<style>` cascade now honors `!important` and resolves `revert`/`revert-layer`](2026-07-22-svg-style-cascade-now-honors-important-and-resolves-revert-rever.md)
- [SVG paint/geometry properties now accept the CSS-wide keywords in `<style>` rules](2026-07-22-svg-paint-geometry-properties-now-accept-the-css-wide-keywords-i.md)
- [Guaranteed-invalid `var()` in SVG styling now computes to inherited/initial instead of falling through](2026-07-22-guaranteed-invalid-var-in-svg-styling-now-computes-to-inherited.md)
- [`@property` registrations now honored in SVG `var()` resolution](2026-07-22-property-registrations-now-honored-in-svg-var-resolution.md)
- [Charts.css showcase + eight enabling CSS-correctness fixes](2026-07-22-charts-css-showcase-eight-enabling-css-correctness-fixes.md)
- [`box-shadow` painting (outset/inset, offset/blur/spread, multiple layers, `border-radius`), with a vector-blur approximation](2026-07-22-box-shadow-painting-outset-inset-offset-blur-spread-multiple-lay.md)
- [`aspect-ratio`](2026-07-22-aspect-ratio.md)
- [`clip-path` with CSS basic shapes](2026-07-22-clip-path-with-css-basic-shapes.md)
- [CSS logical box-model properties](2026-07-22-css-logical-box-model-properties.md)
- [Full `@property` support + a `<style>`-rawtext concatenation bug fix](2026-07-22-full-property-support-a-style-rawtext-concatenation-bug-fix.md)
- [Spec-correct CSS defaulting: one initial-value store + uniform seed; Layer B made longhand-only](2026-07-22-spec-correct-css-defaulting-one-initial-value-store-uniform-seed.md)
- [SVG styling unified onto the real CSS cascade; inline `<svg><style>` fixed](2026-07-22-svg-styling-unified-onto-the-real-css-cascade-inline-svg-style-f.md)

### 2026-07-21

- [Per-page left/right `@page` margins now reflow content to the page's own width](2026-07-21-per-page-left-right-page-margins-now-reflow-content-to-the-page.md)
- [Clip-shape `transform` inside `<clipPath>` now applied](2026-07-21-clip-shape-transform-inside-clippath-now-applied.md)
- [Monochrome emoji / astral codepoints via cmap format-12, and a `Rune`-based CID pipeline](2026-07-21-monochrome-emoji-astral-codepoints-via-cmap-format-12-and-a-rune.md)
- [Removed the dead `idName`/`fontData` font-realization chain](2026-07-21-removed-the-dead-idname-fontdata-font-realization-chain.md)
- [Per-character font matching / `@font-face` `unicode-range`](2026-07-21-per-character-font-matching-font-face-unicode-range.md)
- [Named-page `@page` styles leaking onto later default pages](2026-07-21-named-page-page-styles-leaking-onto-later-default-pages.md)

### 2026-07-20

- [Keep-with-next stranding a heading before a relocated table/thead](2026-07-20-keep-with-next-stranding-a-heading-before-a-relocated-table-thea.md)
