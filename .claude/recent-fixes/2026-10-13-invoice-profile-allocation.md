# Per-box allocation found by profiling the 1,000-invoice document

invoices-l (1,000 pages of tables, flex address blocks, repeated headers; 1.6 MB of HTML) allocated 2.4 GB per render, ~2.4 MB a
page, with GC pauses a third of the run. The profile was flat - no single cost - so this is a batch of the per-box ones, each
sound on its own and each found by an allocation sample, not by reading:

- **`DerivedStyle.ActualFilterFunctions`/`ActualBackdropFilterFunctions`** tokenized and parsed `none` for every box
  (~220 MB over two renders); `none`/empty answers the same empty result without the lexer.
- **`CssBox.Overflow`** built a new `CssProperty<Overflow>` on every read - it is asked by most layout and paint predicates for every
  box, and was the largest allocation under the fragment emitter's `ClipOf`. Four shared instances.
- **`StyleDeclaration`'s `foreach`** went through `Children.OfType<Property>()` boxed into `IEnumerator<IProperty>` for every
  declaration list the cascade walks (per box, per matched rule, twice for the important pass): a public struct enumerator over
  the indexable `ChildList`; the interface still gets the old enumerator.
- **`CssBidiParagraphResolver`**: plain ASCII in a left-to-right paragraph with no overrides gets zero levels, "Latin" (or "Common"
  when there is no letter), no joining and no USE/Khmer categories written out directly. That is the value the three analyses
  give, pinned over 3,000 random ASCII strings against the real ones (`CssBidiParagraphResolverAsciiTests`); the old path still
  runs for anything else (Arabic in the same document is tested).
- **`StackingOrder.ByLayers`** built a dictionary and an `OrderBy` per painted box; one layer (no z-index anywhere) is a list.
- **`FragmentPainter.PaintBackground`** has local functions that capture its parameters, so every call - for every box, background
  or not - allocated a closure. A thin wrapper returns early when there is neither a visible colour nor an image layer; the
  work is in `PaintBackgroundCore`. (A closure is created on method entry, so an early `return` inside the same method does not help.)
- **Border radius, background layers, text indent**: the single-value case (`0`, `8px`, one layer, the initial `0`) no longer
  splits or tokenizes (`FirstCssValue`/`SecondCssValue`, `SplitLayers`, `EnsureTextIndentResolved`).

**Measured** (warm, in-process): invoices-l 2,367 -> 1,723 MB per render (wall ~6.4 -> ~4.7 s best case; this machine is noisy),
table-m 664 -> 426 MB, prose-m 327 -> 241 MB. What is left is mostly the fragment tree itself (`BuildDraftCore`: a `Draft` and
three lists per box per page).

**Evidence.** Release suite green; PDF words and drawings identical to main on 19 benchmark documents, 199 fuzz documents and all
220 showcase PDFs; 0-warning rebuild.
