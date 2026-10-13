# Allocation left in the fragment emitter and the per-run text path

Measured warm, one render, in-process: `invoices-l` 1,715 -> 1,531 MB, `multicol-l` 3,032 -> 2,392 MB, `prose-l` 2,384 -> 1,776 MB,
`table-m` 437 -> 399 MB. Output is unchanged: PyMuPDF words and drawings identical on the 19 benchmark documents, the fuzz corpus and
all 220 showcase PDFs. The pieces, largest first:

- **`PaintColor` had no `IEquatable<PaintColor>`.** Every `==` on it (the operator called `Equals(object)`) and every lookup in
  `RenderContext`'s `Dictionary<PaintColor, Brush>` boxed the struct: 105 MB of `PaintColor` objects in the `multicol-l` profile. The typed
  `Equals(PaintColor)` is public API (`PublicApi.txt` carries it). A type allocated that is a struct in the type list of an allocation
  profile is a boxing site; look there before looking at the named frame.
- **Line breaking allocated about 40 bytes per character per run.** `LineBreakAlgorithm` built a `Unit[]` and `ScalarText` two `int[]` for every
  run of text. They are now this thread's own arrays, reused by the next run and capped at 4,096 characters so a huge paragraph is not
  retained. A `[ThreadStatic]` array, not `ArrayPool<Unit>`: ReadyToRun cannot precompile framework generics over our own value types, which is
  what made startup JIT a dictionary per enum before the keyword maps became int-indexed. The answer must not depend on what the thread broke
  before (stale entries past a shorter text's end): `LineBreakScratchBufferTests` pins that against a fresh thread.
- **`Draft` (one per box per fragmentainer, alive until `Finish`)**: took copies of three lists `BuildDraftCore` had just built; it now adopts
  them, and an empty one is a shared instance. The nine members only a displaced, fixed-position or continuation-shell fragment sets (~136
  bytes) moved to a side object. `BuildDraftCore` also allocated a closure on every call because `() => IsInSlicedContent(box)` captured `box`;
  `ClaimsLine` takes the box instead of a delegate.
- **`RectanglesView` is a `Dictionary`, not `IReadOnlyDictionary`**, so iterating it uses the struct enumerator (the interface boxed one per
  call, in `BuildDraftCore`, `SliceGeometriesOf` and `ClipSourceBoundsOf`). `BoxGeometrySnapshot.CaptureBox` read `box.Rectangles`, which
  creates the dictionary for a box that has none, and grew its lists by doubling although the counts are known.
- **The glyph hex literal** is built in a 512-character stack buffer and appended, no string per run (a longer literal takes an array).
- **`PdfCanvas.ToXSolidBrush`** returned a new `SolidBrush` for every text draw; it returns the render context's cached one.
- **`CssLayoutEngineColumns`** built its child list with `Where(...).ToList()` on every pass; it is sized once.

## Tried and not kept

- **Reference-hash sets on `CssBox`**: a trace put `RuntimeHelpers.GetHashCode` at 17-22% self time, under the emitter's and painter's
  `HashSet<CssBox>`/`HashSet<BoxFragment>`. Overriding `CssBox.GetHashCode` with its `Id` and dropping `ReferenceEqualityComparer` changed
  nothing measurable on `math-l` (1.88/2.13 s before, 2.03/2.09 s after). It is GC pause charged to the running frame, as it was
  for `List.RemoveAt` and the MathML serializer earlier: a sampling profile cannot tell a slow frame from one that happened to be running
  when a collection began, so a change is judged on allocation and an interleaved A/B run, not on the frame's share; reverted.
- **GC settings on the AOT CLI** (`DOTNET_gcServer=1`, `GCHeapCount=4`, `GCgen0MaxBudget`): no gain on `invoices-l`, a loss on `invoices-m`.

## Not done

`math-l` (4.6x Typst) is dominated by per-element `CssBox`/`DerivedStyle` for ~100,000 MathML descendants. The math builder reads each one's
cascaded colour, font size and font, so skipping them means building the math tree from the parser and resolving those three values per
element: a design change, not an allocation fix.
