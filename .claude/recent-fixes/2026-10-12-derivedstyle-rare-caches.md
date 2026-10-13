# DerivedStyle: rare caches live in side objects

`DerivedStyle` is one per `CssBox` and was 928 bytes: 906 bytes of cache fields, most of them for things an ordinary box never
has. Measured after layout on three benchmark documents, border colors, column rule, outline, radii, transform, perspective, the
font-variant caches and the shaping settings were populated on 0% of math and prose boxes. 174,000 boxes x 928 B was 24% of the
live heap of the MathML benchmark, and its GC pause tracks live heap.

**The change.** Those fields moved into two nested classes allocated on first use: `PaintExtras` (border colors, column rule,
outline, radii, transform, perspective: ~390 B) and `ShapingExtras` (font-variant caches, feature settings, text shaping
settings, small-caps and sub/superscript fonts, codepoint font cache, palette: ~230 B). `DerivedStyle` itself is 344 B. After
layout of the 174,000-box document 11,550 boxes hold paint extras and 32,073 hold shaping extras.

**Traps.**
- A read that computes a cache allocates the extras, so a read every box makes defeats the split. `HasTransform`,
  `IsTransformed`, `ActualTransformMatrix`, `ActualTransform4` and `ActualPerspective` are asked of every box (stacking-context
  predicate, paint), so they answer `none` without caching and without touching the extras. Check any new reader the same way:
  `HasPaintExtras`/`HasShapingExtras` show what an ordinary box holds.
- `Invalidate*` methods must not allocate the extras just to reset a value they do not hold; they test the field first.
- Colors (`_actualColor`, `_actualBackgroundColor`) and the font resolution fields stay inline: every text box reads them.

**Measured** (warm, in-process): math-l GC heap 604 -> 492 MB, allocation 1,151 -> 1,059 MB per render, GC pause ~950 -> 790 ms,
wall ~2.6 -> ~2.1 s; table-m heap 274 -> 240 MB; prose-m unchanged within noise.

**Evidence.** Release suite green; PyMuPDF words+drawings identical to main on 19 benchmark documents, 80 fuzz documents and
all 220 TestHarness showcase PDFs (which exercise transforms, radii, outlines and the font variants); 0-warning rebuild.
