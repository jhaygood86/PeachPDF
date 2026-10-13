# Boxes that make the same style changes share one ComputedStyle

A box's style is a `ComputedStyle` of 16 immutable area records, and every property the cascade set cloned the area and then the
`ComputedStyle` pointing at it. Thousands of boxes of one kind made the same changes from the same starting point and each kept
a private, equal copy. Measured on a 14,403-box document: 14,403 `ComputedStyle`s of which 16 were distinct by content, 2,402
`TextArea`s of which 2, 1,201 `FontArea`s of which 3, and every other area already shared.

**The change.** `StyleTransitionCache` (one per `HtmlContainerInt`) remembers "this style, this setter, this value -> that
result". The generated setters and `InheritStyle` route through `SetPropertyValueShared`/`AdoptAreaShared`, so the next box making
the same change gets the first box's result. After: 18 distinct `ComputedStyle`s, 4 `TextArea`s, 3 `FontArea`s.

**Traps.**
- Only styles that took part from the start are followed (the `Default` of each record, registered by `StyleTransitionCache.Root`,
  and anything the cache produced). Anything else is cloned as before, so a chain unique to one box costs nothing extra.
- `CustomProperties` is a dictionary mutated in place and keyed by reference, so its writes stay on the unshared path; sharing a
  style that holds one would let one box's `--x` write reach another's.
- Keyed on the `static` lambda delegate at the call site (one object per site) - a non-static or capturing lambda would defeat it
  silently (nothing breaks, nothing is shared).
- A null value cannot key a dictionary and is cloned. The cache stops remembering at 200,000 transitions and clones from there.

**Measured** (warm, in-process, net10.0): math-l 2.7 s / 1.72 GB / 1,144 ms GC pause -> 2.44 s / 1.47 GB / 932 ms; table-m
1.4-1.5 s / 1,096 MB -> 1.24 s / 848 MB; prose-m 462 -> 441 MB.

**Evidence.** Suite green in Release (16,457); PyMuPDF words+drawings identical to a main build on 19 benchmark documents; 80-document
differential fuzz identical; 0-warning solution rebuild.
