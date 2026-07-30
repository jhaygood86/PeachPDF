# `ComputedStyle` split into per-area records, with whole-area reuse on inherit

## The load-bearing idea

`ComputedStyle` held all ~131 cascaded CSS properties in one immutable record. Every `InheritStyle` call
cloned the *entire* record once per inheritable property that differed from a box's own default - so a
`body { font-family: ...; color: ...; }` rule meant every descendant box in the whole document paid for one
131-property clone per differing property, even though only 1-2 properties actually changed.

This splits `ComputedStyle` into 16 smaller "area" records (`ComputedStyleAreas.cs`), each covering roughly
one CSS module, sharing a generic copy-on-write extension method (`ComputedStyleCow`). `CssBox.InheritStyle`
now adopts a whole area instance directly by reference from the parent for the five fully-inherited areas
(`Font`, `Text`, `Table`, `List`, `Pagination`) instead of copying property-by-property - safe because the
whole design is copy-on-write throughout, so a parent's area instance is never mutated after being handed to
a child. A subtree that never overrides anything in, say, `Font` ends up with every box's `Font`
`ReferenceEquals` the same object as the ancestor that last set it.

Every area's `Default` sources its per-property defaults from `CssDefaults.GetInitialValue` (the same store
`DomParser.CascadeApplyStyles` already uses to seed a real element's initial values) instead of redeclaring
literals - which required extending `CssDefaults` with ~23 previously-missing entries (all of Flexbox/Grid,
`object-fit`/`object-position`, `font-palette`, `page`), closing a real gap where e.g. `flex-grow: initial`
was a silent no-op (`DomParser`'s `value is null` short-circuit on an unknown property).

**Deliberate spec-compliance fix, not a side effect to revert:** `box-sizing` was incorrectly treated as
inherited (`CssDefaults.InheritedProperties` listed it, `InheritStyle`'s always-section copied it). CSS Box
Sizing 3 §3 defines it as `inherited: no`. It now lives in the non-inherited `BoxModel` area and is no longer
copied by the always-section - but it *is* still copied by `InheritStyle`'s `everything: true` branch (used
only for structural duplicates of the same source box - `CssProxyBox`'s repeated header/footer,
`DomParser`'s inline/block split), the same way `box-decoration-break`/the break properties/`PdfTagType`
already are, since a structural duplicate needs the source element's own resolved value even though it isn't
a real ancestor→descendant inheritance case.

## What was found by running it, not by reading it

**A post-change review pass caught a real bug this refactor introduced**, missed by the full test suite:
`box-sizing` was correctly removed from `InheritStyle`'s always-section but never added to the `everything`
branch, so a `CssProxyBox` repeated `<thead>`/`<tfoot>` or an inline/block split half would silently revert
to `content-box` regardless of the source element's declared `box-sizing`. Fixed by adding it to the
`everything` branch's `boxModel` chain, with `ComputedStyleTests.InheritStyle_Everything_CopiesBoxSizing`
locking it in. Nothing in the pre-existing suite caught this because nothing exercised `box-sizing` through
`InheritStyle`'s `everything` path at all.

**The benchmark theory ("most boxes only set a handful of properties, so allocations should drop
dramatically") held up in the math but not in either benchmarked workload.** A dedicated synthetic document
(300 chains of 40 plain, unstyled nested `<div>`s under one customized wrapper each - built specifically to
isolate the "deep chain of untouched inheritance" case) showed essentially flat allocation and wall time
versus the prior single-record `ComputedStyle` (~130.35 GB / 18.996s before this split vs ~130.53 GB /
19.485s after, across 5 iterations each) - not the dramatic drop expected. The existing 500-article
`ContractBench` document was similarly flat (~228.13 GB / 92.83s before vs ~228.29 GB / 94.53s after).

The reason: **~130 GB allocated to render a ~12,600-element, 163 KB document is roughly 10 MB per element** -
overwhelmingly dominated by something else in the render pipeline (layout/measurement passes, font metrics,
PDF content-stream writing), not by cascade/inheritance bookkeeping. Whatever `InheritStyle`'s own allocation
this change saves (real - the old single-record model needed one full 131-property clone per differing
*property*; the area split needs at most one small ~17-reference outer-container clone per differing *area*,
and zero area clones at all for a box that inherits an area unchanged) is a rounding error against that much
larger, unrelated allocation source. The 73-showcase-document workload (a more varied, more representative
corpus than either large synthetic document) did show a real, if modest, improvement over the prior
single-record state - allocated bytes and peak GC heap both down, though not all the way back to the
pre-`ComputedStyle`-refactor baseline. **The mechanism is correct and does what it's supposed to
(verified directly via `ReferenceEquals` assertions in `ComputedStyleTests`), but "dramatically reduce
allocations, memory usage, and wall time" does not hold for either of the two large-document benchmarks -
record this honestly rather than reporting a win that isn't there.**

**A post-change review also flagged a real (if low-severity) performance self-defeat**: every one of the
131 property setters' outer-level `ComputedStyle`↔area swap, and `InheritStyle`'s whole-area adoptions, used
`ComputedStyleCow.SetPropertyValue`'s *structural* (record) equality - comparing every field of the area
(up to 19 for `BoxModelArea`) on every real write, when the two operands being compared are guaranteed to
either be the exact same object (no-op) or already-known-different (a real change happened one level down) -
a reference check gives the identical answer for free. Fixed by adding a second extension method,
`AdoptArea`, that compares by reference instead, used at every "does this new area replace the box's
current one" call site while `SetPropertyValue` stays value-based at the true leaf-property level (where a
caller passes a fresh literal each time, so reference comparison would never detect a no-op). This also
makes `InheritStyle`'s whole-area adoption genuinely reference-unifying even when a child's own area happens
to be content-equal-but-object-distinct from its parent's - closing a gap where the doc comment's
"ends up `ReferenceEquals` the same object" promise wasn't quite universally true before.

## Deliberately not done

**`vertical-align`'s non-inheritance is not fixed here.** The review found it's CSS-spec `inherited: no`
(CSS 2.1 §10.8.1), but this codebase has always treated it as inherited - a pre-existing deviation this
change's area grouping surfaced (it sits in the otherwise-100%-inherited `TextArea`) rather than introduced.
Fixing it means auditing `CssLayoutEngine.ApplyVerticalAlignment`'s existing dependence on today's behavior -
a real layout-behavior change, out of scope for a change about allocation/architecture. Recorded as
[#530](https://github.com/jhaygood86/PeachPDF/issues/530) and
`.claude/accepted-gaps/vertical-align-is-treated-as-inherited.md`.

**`InheritStyle`'s `everything` branch stays property-by-property, not whole-area**, even for areas that
otherwise look fully inherited-by-this-branch (e.g. `Background`) - cross-checking confirmed several areas
are only partially covered by that branch's historical list (`Background` is missing `BackgroundSize`,
`Border` is missing `BoxShadow`), so adopting a whole area there would silently copy properties `everything`
has never covered. Each property in that branch is still individually routed through its owning area's
two-level copy-on-write, preserving the exact existing list (including its gaps) with zero new risk.

**`row-gap`/`column-gap`'s stored initial value (`"0"`, not the spec's `"normal"`) and `justify-items`'s
(`"normal"`, not the spec's `"legacy"`) were not changed** - both are pre-existing choices carried over
unchanged from the prior single-record `ComputedStyle`'s own literals, not introduced by sourcing defaults
from `CssDefaults`. `column-gap`'s `"0"` is already compensated for by `CssLayoutEngineColumns` (which
treats the shared default as multicol's 1em), and `legacy <side>` alignment isn't implemented at all, so
`"normal"` is the pragmatic stand-in. Left as-is; the `CssDefaults` comment now discloses both explicitly
instead of implying they're exact spec values.

## Evidence

Full net8.0 suite: 6990 passed / 0 failed / 9 skipped. Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`.
Diff coverage 96% (gate is 90%) against `claude/cssbox-computed-style-refactor-p5fmvw`.
