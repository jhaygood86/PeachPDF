# Text and anonymous boxes share one computed style per parent style

`invoices-l` (158,000 boxes per render, about half of them text or anonymous boxes): warm render -5% in process (best of 8: 3.62/3.38/3.33 s
to 3.29/3.20/3.16 s), about -2% as the AOT CLI (5.13/5.05/5.15 s to 4.92/5.03/5.06 s), allocation -2%. Output is unchanged (benchmark documents,
fuzz corpus and all 220 showcases identical to main).

A text box or anonymous box has no element, so no rule or attribute reaches it (CSS Cascade 4 §1.1/§7.2: it only inherits). What the cascade
makes of it is a function of the style it comes in with and its parent's, so `CascadeApplyStyles` keeps the result in
`CssValueParser.InheritedOnlyStyles`, keyed by the two `ComputedStyle` references, and the next such box under a parent of that style adopts it
(`CssBox.AdoptComputedStyle`) and skips straight to the steps that are per box (placeholder, logical properties, first-line). Computed styles are
immutable and already shared, so boxes holding the same instance is nothing new. References, not equality, because the styles are interned by the
transition cache and a deep comparison would cost what the cache saves.

Left out, and why:
- **A parent with custom properties**: `InheritStyle` gives each child its own copy of the dictionary, which a shared style would not.
- **A document being animated** (`AnimationProgress`): sampled values are applied per box.
- **Pseudo-element boxes and anything with an element**: a rule can reach them, and matching a pseudo-element selector creates the box, so
  it is not a function of the two styles.

`DomParser.VerifySharedStyles` (a test hook) makes a hit run the cascade anyway and throw if it disagrees. The whole suite and the benchmark
documents were run that way before the cache was trusted (76,636 hits on `invoices-l`, no mismatch), and `InheritedOnlyStyleTests` does it for a
corpus of structures that stress it: tables, lists, flex/grid anonymous items, floats, `display: contents`, pseudo-elements, writing modes, forms,
bidi.

## The profile that led here, and what it ruled out

The sampling profiler put 45% of non-GC time under `RuntimeHelpers.GetHashCode`; giving `CssBox` the cheap `Id` hash changed nothing (best of 8,
interleaved). A suspending sampler stops at safe points, so a call into the runtime collects the time of the code around it: do not read leaf
frames from it. Stopwatch phases over one warm render of `invoices-l` (about 4 s): parse 0.2, cascade 0.7-0.85, box layout 1.3, emitter 0.9, paint
0.65-0.9.

## Not done

The same idea for elements, which are the other half of the boxes. Siblings under one parent with the same tag and attributes are 52% of the elements
of `invoices-l` (40,786 of 77,651) and 71% of `table-l`; across parents nearly all of them. But what matches an element depends on its position
(`:nth-child`, `+`), on its ancestors (descendant combinators) and on pseudo-elements (matching creates the box), so a cache needs a per-signature
check that no candidate rule is position- or pseudo-sensitive, and for sharing across parents that no candidate has a combinator. The ceiling is
about the same size as this change (the cascade is ~20% of a render and the matching part of it a third), for a much larger blast radius.
